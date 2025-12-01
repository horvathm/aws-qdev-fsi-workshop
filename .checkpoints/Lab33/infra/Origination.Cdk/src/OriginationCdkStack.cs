using System;
using System.IO;
using System.Collections.Generic;
using Constructs;
using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.Ecr.Assets;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.APIGateway;
using Amazon.CDK.AWS.Events;
using Amazon.CDK.AWS.SSM;

namespace OriginationCdk
{
    public class OriginationCdkStack : Stack
    {
        internal OriginationCdkStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
        {
            #region infrastructure
            // First get the VPC lookup
            var labVpc = Vpc.FromLookup(this, "LabVpc", new VpcLookupOptions
            {
                VpcName = "microsvc-workshop-VPC",
                SubnetGroupNameTag = "aws-cdk:subnet-type"
            });

            // New security group called OriginationSG in the labVpc that allows outbound traffic
            SecurityGroup originationSG = new SecurityGroup(this, "OriginationSG", new SecurityGroupProps
            {
                Vpc = labVpc,
                AllowAllOutbound = true
            });
            #endregion

            #region Fargate


            // Create the Fargate Cluster in the vpc, name it OriginationCluster
            Cluster originationCluster = new Cluster(this, "OriginationCluster", new ClusterProps
            {
                Vpc = labVpc
            });

            // Create CloudWatch LogGroup called OriginationSvcLogs, log group name is origination-logs. Set retention to 3 days
            LogGroup originationSvcLogs = new LogGroup(this, "OriginationSvcLogs", new LogGroupProps
            {
                LogGroupName = "/origination-logs",
                Retention = RetentionDays.THREE_DAYS,
                RemovalPolicy = RemovalPolicy.DESTROY
            });

            // Service task definition OriginationTaskDefn for OriginationSvc
            // Use FARGATE, set cpu = 512 and memory = 1024
            // Use Linux on ARM64
            // For the TaskRole, look up existing IAM role called OriginationTaskRole
            TaskDefinition originationTaskDefn = new TaskDefinition(this, "OriginationTaskDefn", new TaskDefinitionProps
            {
                Family = "DigitalOriginationMicrosvc",
                Compatibility = Compatibility.FARGATE,
                Cpu = "512",
                MemoryMiB = "1024",
                TaskRole = Role.FromRoleName(this, "EcsTaskRole", "WorkshopOriginationTaskRole"),
                ExecutionRole = Role.FromRoleName(this, "EcsTaskExecutionRole", "WorkshopEcsTaskExecutionRole"),
                RuntimePlatform = new RuntimePlatform
                {
                    OperatingSystemFamily = OperatingSystemFamily.LINUX,
                    CpuArchitecture = CpuArchitecture.ARM64
                },
            });

            // Build the Docker Image OriginationAdotEcrAsset to use with Amazon ECR, target Linux on ARM64 
            // Dockerfile path would be in "/workshop-folder/Origination/src/Otel"
            var originationAdotEcrAsset = new Amazon.CDK.AWS.Ecr.Assets.DockerImageAsset(this, "OriginationAdotEcrAsset", new DockerImageAssetProps
            {
                Directory = Path.GetFullPath("/workshop-folder/Origination/src/Otel"),
                Platform = Platform_.LINUX_ARM64
            });

            // Add the originationAdotEcrAsset container into the task definition originationTaskDefn
            // Call this adotCollectorContainer
            // Set memory limit to 256, cpu to 128 and mark this as essential container
            // The otel config file is in /etc/ecs/otel-config.yaml
            // For logging, use originationSvcLogs and set the stream prefix as adot-logs
            ContainerDefinition adotCollectorContainer = originationTaskDefn.AddContainer("AdotCollectorContainer", new ContainerDefinitionOptions
            {
                Image = ContainerImage.FromDockerImageAsset(originationAdotEcrAsset),
                MemoryLimitMiB = 256,
                Cpu = 128,
                Essential = true,
                Command = new[] { "--config=/etc/ecs/otel-config.yaml" },
                Environment = new Dictionary<string, string>() {
                    {"AWS_REGION", this.Region}
                },
                Logging = new AwsLogDriver(new AwsLogDriverProps
                {
                    LogGroup = originationSvcLogs,
                    StreamPrefix = "adot-logs"
                })
            });

            // Build the Docker Image OriginationEcrAsset to use with Amazon ECR, target Linux on ARM64 
            // Dockerfile path would be in "/workshop-folder/Origination/src/Origination"
            var originationEcrAsset = new Amazon.CDK.AWS.Ecr.Assets.DockerImageAsset(this, "OriginationEcrAsset", new DockerImageAssetProps
            {
                Directory = Path.GetFullPath("/workshop-folder/Origination/src/Origination"),
                Platform = Platform_.LINUX_ARM64
            });

            // Add the originationEcrAsset container into the task definition originationTaskDefn
            // Set memory limit to 1024 and mark this as essential container
            // Use the log group originationSvcLogs, set the stream prefix to api-logs
            // Set the following environment variables: 
            //   {"ASPNETCORE_URLS", "http://+:6268"},
            //   { "ASPNETCORE_ENVIRONMENT", "Development"}
            // Define health check using CMD wget --no-verbose --tries=1 --spider http://localhost:6268/hc || exit 1
            // Health check internal is 30s, timeout 10s, start period 30s, retry up to 3 times
            // Map port 6268 to container
            ContainerDefinition originationContainer = originationTaskDefn.AddContainer("OriginationContainer", new ContainerDefinitionOptions
            {
                Image = ContainerImage.FromDockerImageAsset(originationEcrAsset),
                MemoryLimitMiB = 1024,
                Essential = true,
                Logging = new AwsLogDriver(new AwsLogDriverProps
                {
                    LogGroup = originationSvcLogs,
                    StreamPrefix = "api-logs"
                }),
                Environment = new Dictionary<string, string>() {
                    {"ASPNETCORE_URLS", "http://+:6268"},
                    {"ASPNETCORE_ENVIRONMENT", "Development"}
                },
                HealthCheck = new Amazon.CDK.AWS.ECS.HealthCheck
                {
                    Command = new[] {
                        "CMD-SHELL",
                        "wget --no-verbose --tries=1 --spider http://localhost:6268/hc -O /dev/null 2>&1 || exit 1"
                        },
                    Interval = Duration.Seconds(30),
                    Timeout = Duration.Seconds(10),
                    Retries = 3,
                    StartPeriod = Duration.Seconds(60)
                },
                PortMappings = new[] { new PortMapping { ContainerPort = 6268 } }
            });
            
            // Set the adotCollectorContainer as sidecar to originationContainer
            // Add dependency to ensure ADOT collector starts before the application container
            originationContainer.AddContainerDependencies(new ContainerDependency
            {
                Container = adotCollectorContainer,
                Condition = ContainerDependencyCondition.START
            });

            // Create the ECS Fargate Service using the ECS cluster, task definition and security group defined above
            // Set desired count to 2, deploy them onto private subnets one per AZ and ensure all containers are healthy
            FargateService ecsFargateService = new FargateService(this, "EcsFargateService", new FargateServiceProps
            {
                Cluster = originationCluster,
                DesiredCount = 2,
                TaskDefinition = originationTaskDefn,
                SecurityGroups = new[] { originationSG },
                MaxHealthyPercent = 200,
                MinHealthyPercent = 100,
                VpcSubnets = new SubnetSelection
                {
                    SubnetType = SubnetType.PRIVATE_WITH_EGRESS,
                    OnePerAz = true
                }
            });
            #endregion

            #region Vpc Link
            // Now we need to expose this to API gateway via private integration. For this we need an NLB
            // Define the NLB security group in the same vpc
            // Allow inbound

            // Here we use NLB because VPN Gateway private integration for REST API needs NLB
            SecurityGroup nlbSG = new SecurityGroup(this, "NlbSG", new SecurityGroupProps
            {
                Vpc = labVpc,
                AllowAllOutbound = true,
                SecurityGroupName = "NlbSG"
            });

            // Allow the originationSG to receive inbound Tcp connection on port 6268 from nlbSG
            originationSG.Connections.AllowFrom(nlbSG, Port.Tcp(6268), "Ingress from nlbSG");

            // Create Network Load Balancer OriginationNlb in the labVpc
            // Ensure it is NOT internet facing, set the security group to nlbSG
            // Add a listener on port 80, Ensure we select one subnet per AZ
            NetworkLoadBalancer originationNlb = new NetworkLoadBalancer(this, "OriginationNlb", new NetworkLoadBalancerProps
            {
                Vpc = labVpc,
                InternetFacing = false,
                SecurityGroups = new[] { nlbSG },
                EnforceSecurityGroupInboundRulesOnPrivateLinkTraffic = false, //allows VPC link traffic
                VpcSubnets = new SubnetSelection
                {
                    SubnetType = SubnetType.PRIVATE_WITH_EGRESS,
                    OnePerAz = true
                }
            });
            NetworkListener listener = originationNlb.AddListener("InternalListener", new BaseNetworkListenerProps { Port = 80 });

            //Route the listener to ecsFargateService Service
            listener.AddTargets("EcsFargateServiceTarget", new AddNetworkTargetsProps
            {
                Port = 80,
                Targets = new[] { ecsFargateService.LoadBalancerTarget( new LoadBalancerTargetOptions {
                    ContainerName = "OriginationContainer",
                    ContainerPort = 6268
                })}
            });

            // Use API Gateway as proxy so it can be called by EventBridge
            // For better security, we'll do this as private API
            // First, create API Gateway VPC Link to the originationNlb 
            VpcLink apiGwVpcLink = new VpcLink(this, "ApiGwVpcLink", new VpcLinkProps
            {
                VpcLinkName = "OriginationVpcLink",
                Targets = new[] { originationNlb }
            });
            apiGwVpcLink.ApplyRemovalPolicy(RemovalPolicy.DESTROY);
            #endregion

            #region API Gateway

            // Create the API Gateway REST API
            // Make sure to mark it as private API
            // Allow EventBridge to execute the api
            // Enable CORS on the API
            // Set default method authorization to IAM
            // Don't deploy this API yet, we'll want to first add resources and methods
            RestApi originationApi = new RestApi(this, "OriginationApi", new RestApiProps
            {
                RestApiName = "OriginationApi",
                Deploy = false,
                RetainDeployments = false,
                EndpointConfiguration = new EndpointConfiguration
                {
                    Types = new[] { EndpointType.REGIONAL },
                },
                DefaultCorsPreflightOptions = new CorsOptions
                {
                    AllowOrigins = Cors.ALL_ORIGINS,
                    AllowMethods = Cors.ALL_METHODS,
                    AllowHeaders = new[] { "Content-Type" }
                },
                DefaultMethodOptions = new MethodOptions
                {
                    AuthorizationType = AuthorizationType.IAM,
                },
                BinaryMediaTypes = new[] {
                    "multipart/form-data",
                    "image/jpeg",
                    "image/jpg",
                    "image/png",
                    "application/pdf"
                }
            });
            originationApi.ApplyRemovalPolicy(RemovalPolicy.DESTROY);

            #region API Resources & methods

            // Create API resources
            // Create base resource called application
            // Create a child resource underneath application with {applicationId} parameter
            // Create 3 child resources {applicationId} called status, customer, file
            var applicationResource = originationApi.Root.AddResource("application");
            var applicationIdResource = applicationResource.AddResource("{applicationId}");
            var statusResource = applicationIdResource.AddResource("status");
            var customerResource = applicationIdResource.AddResource("customer");
            var fileResource = applicationIdResource.AddResource("file");


            //Expose the POST method against applicationResource for the UI to handle application creation
            applicationResource.AddMethod("POST", new Integration(new IntegrationProps
            {
                Type = IntegrationType.HTTP_PROXY,
                IntegrationHttpMethod = "POST",
                Options = new IntegrationOptions
                {
                    ConnectionType = ConnectionType.VPC_LINK,
                    VpcLink = apiGwVpcLink
                },
                Uri = $"http://{originationNlb.LoadBalancerDnsName}/application"
            }));

            //Expose GET method against applicationIdResource for the UI to retrieve application details
            //Uri should be /application/{applicationId} and simply proxy it to the WebAPI
            applicationIdResource.AddMethod("GET", new Integration(new IntegrationProps
            {
                Type = IntegrationType.HTTP_PROXY,
                IntegrationHttpMethod = "GET",
                Options = new IntegrationOptions
                {
                    ConnectionType = ConnectionType.VPC_LINK,
                    VpcLink = apiGwVpcLink,
                    RequestParameters = new Dictionary<string, string>
                    {
                        ["integration.request.path.applicationId"] = "method.request.path.applicationId"
                    }
                },
                Uri = $"http://{originationNlb.LoadBalancerDnsName}/application/{{applicationId}}"
            }), new MethodOptions
            {
                RequestParameters = new Dictionary<string, bool> { ["method.request.path.applicationId"] = true }
            });

            //Expose POST method against customerResource for the UI to handle customer details submission
            //Uri should be /application/{applicationId}/customer and simply proxy it to the WebAPI
            customerResource.AddMethod("POST", new Integration(new IntegrationProps
            {
                Type = IntegrationType.HTTP_PROXY,
                IntegrationHttpMethod = "POST",
                Options = new IntegrationOptions
                {
                    ConnectionType = ConnectionType.VPC_LINK,
                    VpcLink = apiGwVpcLink,
                    RequestParameters = new Dictionary<string, string>
                    {
                        ["integration.request.path.applicationId"] = "method.request.path.applicationId"
                    }
                },
                Uri = $"http://{originationNlb.LoadBalancerDnsName}/application/{{applicationId}}/customer"
            }), new MethodOptions
            {
                RequestParameters = new Dictionary<string, bool> { ["method.request.path.applicationId"] = true }
            });

            //Expose POST method against fileResource for the UI to handle file upload
            //Uri should be /application/{applicationId}/file and simply proxy it to the WebAPI
            fileResource.AddMethod("POST", new Integration(new IntegrationProps
            {
                Type = IntegrationType.HTTP_PROXY,
                IntegrationHttpMethod = "POST",
                Options = new IntegrationOptions
                {
                    ConnectionType = ConnectionType.VPC_LINK,
                    VpcLink = apiGwVpcLink,
                    RequestParameters = new Dictionary<string, string>
                    {
                        ["integration.request.path.applicationId"] = "method.request.path.applicationId",
                        ["integration.request.querystring.docuType"] = "method.request.querystring.docuType"
                    }
                },
                Uri = $"http://{originationNlb.LoadBalancerDnsName}/application/{{applicationId}}/file"
            }), new MethodOptions
            {
                RequestParameters = new Dictionary<string, bool>
                {
                    ["method.request.path.applicationId"] = true,
                    ["method.request.querystring.docuType"] = true
                },
                RequestValidator = new RequestValidator(this, "FileUploadValidator", new RequestValidatorProps
                {
                    ValidateRequestParameters = true,
                    RestApi = originationApi,
                    RequestValidatorName = "FileUploadValidator"
                }),
                RequestModels = new Dictionary<string, IModel>
                {
                    ["multipart/form-data"] = Model.EMPTY_MODEL
                }
            });

            // Add the validation model for docuType
            var docuTypeModel = new Model(this, "DocuTypeModel", new ModelProps
            {
                RestApi = originationApi,
                ContentType = "application/json",
                Schema = new JsonSchema
                {
                    Type = JsonSchemaType.OBJECT,
                    Properties = new Dictionary<string, IJsonSchema>
                    {
                        ["docuType"] = new JsonSchema
                        {
                            Type = JsonSchemaType.INTEGER,
                            Minimum = 1,
                            Maximum = 3
                        }
                    }
                }
            });

            //Expose GET method against statusResource for the UI to retrieve application status
            //Uri should be /application/{applicationId}/status and simply proxy it to the WebAPI
            statusResource.AddMethod("GET", new Integration(new IntegrationProps
            {
                Type = IntegrationType.HTTP_PROXY,
                IntegrationHttpMethod = "GET",
                Options = new IntegrationOptions
                {
                    ConnectionType = ConnectionType.VPC_LINK,
                    VpcLink = apiGwVpcLink,
                    RequestParameters = new Dictionary<string, string>
                    {
                        ["integration.request.path.applicationId"] = "method.request.path.applicationId"
                    }
                },
                Uri = $"http://{originationNlb.LoadBalancerDnsName}/application/{{applicationId}}/status"
            }), new MethodOptions
            {
                RequestParameters = new Dictionary<string, bool> { ["method.request.path.applicationId"] = true }
            });

            // Expose the POST method against statusResource for EventBridge to handle status update
            // Set authorization to IAM
            // The Uri should be /application/{applicationId}/status and simply proxy it to the WebAPI
            var postStatusMethod = statusResource.AddMethod("POST", new Integration(new IntegrationProps
            {
                Type = IntegrationType.HTTP_PROXY,
                IntegrationHttpMethod = "POST",
                Options = new IntegrationOptions
                {
                    ConnectionType = ConnectionType.VPC_LINK,
                    VpcLink = apiGwVpcLink,
                    RequestParameters = new Dictionary<string, string>
                    {
                        ["integration.request.path.applicationId"] = "method.request.path.applicationId"
                    }
                },
                Uri = $"http://{originationNlb.LoadBalancerDnsName}/application/{{applicationId}}/status"
            }), new MethodOptions
            {
                RequestParameters = new Dictionary<string, bool> { ["method.request.path.applicationId"] = true }
            });

            #endregion

            // Create deployment to prod stage
            var deployment = new Deployment(this, "OriginationApiDeployment", new DeploymentProps
            {
                Api = originationApi,
                Description = $"Deployment for Origination API from cdk stack {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}",

            });

            // Create or update the prod stage
            var prodStage = new Amazon.CDK.AWS.APIGateway.Stage(this,
                "ProdStage",
                new Amazon.CDK.AWS.APIGateway.StageProps
                {
                    StageName = "prod",
                    Deployment = deployment,
                    DataTraceEnabled = true,
                    MetricsEnabled = true,
                    LoggingLevel = MethodLoggingLevel.INFO
                });

            originationApi.DeploymentStage = prodStage;
            #endregion

            #region EventBridge
            var eventBus = EventBus.FromEventBusName(this, "WorkshopEventBus", "workshop-events");
            var rule = new Rule(this, "StatusUpdateRule", new RuleProps
            {
                EventBus = eventBus, 
                RuleName = "StatusUpdate", 
                EventPattern = new EventPattern
                {
                    DetailType = new[] { "application.image.processed", "application.document.processed" }
                }
            });

            var cfnRule = rule.Node.DefaultChild as Amazon.CDK.AWS.Events.CfnRule;
            
            if (cfnRule != null)
            {
                var method = "POST";
                var path = "/application/*/status";
                var stage = "prod";
                var arn = $"arn:aws:execute-api:{Of(this).Region}:{Of(this).Account}:{originationApi.RestApiId}/{stage}/{method}{path}";

                cfnRule.Targets = new[]
                {
                    new Amazon.CDK.AWS.Events.CfnRule.TargetProperty
                    {
                        Arn = arn,
                        Id = "ApiGatewayTarget",
                        RoleArn = Role.FromRoleArn(this, "EventBridgeApiCallerRole", 
                            $"arn:aws:iam::{Of(this).Account}:role/WorkshopEventBridgeApiCaller").RoleArn,
                        HttpParameters = new Amazon.CDK.AWS.Events.CfnRule.HttpParametersProperty
                        {
                            PathParameterValues = new[] { "$.detail.ApplicationId" },
                            HeaderParameters = new Dictionary<string, string>
                            {
                                ["Content-Type"] = "application/json"
                            }
                        },
                        InputTransformer = new Amazon.CDK.AWS.Events.CfnRule.InputTransformerProperty
                        {
                            InputPathsMap = new Dictionary<string, string>
                            {
                                ["doctype"] = "$.detail.DocType",
                                ["status"] = "$.detail.Status",
                                ["remarks"] = "$.detail.Remarks"
                            },
                            InputTemplate = "{ \"DocType\": <doctype>, \"NewStatus\": <status>, \"Remarks\": \"<remarks>\" }"
                        }
                    }
                };
            }

            #endregion

            #region SSM Parameter Store
            // Let's put the various attributes of the API to make it easier for UI to consume
            // This also assist with decoupling

            // Create SSM parameter with the API invoke URL
            var ssmParamInvokeUrl = new StringParameter(this, "OriginationApiInvokeUrl", new StringParameterProps
            {
                ParameterName = "/app/origination/api/invokeurl",
                StringValue = originationApi.Url,
                Description = "Invoke URL for the Origination API",
                DataType = ParameterDataType.TEXT,
                Tier = ParameterTier.STANDARD
            });

            // Create SSM parameter for application resource
            new StringParameter(this, "ApplicationResourceName", new StringParameterProps
            {
                ParameterName = "/app/origination/api/paths/application",
                StringValue = applicationResource.Path,
                Description = "Name of the Application Resource in Origination API",
                DataType = ParameterDataType.TEXT,
                Tier = ParameterTier.STANDARD
            });

            // Create SSM parameter for customer resource
            new StringParameter(this, "CustomerResourcePath", new StringParameterProps
            {
                ParameterName = "/app/origination/api/paths/customer",
                StringValue = customerResource.Path,
                Description = "Name of the Customer Resource in Origination API",
                DataType = ParameterDataType.TEXT,
                Tier = ParameterTier.STANDARD
            });

            // Create SSM parameter for file resource
            new StringParameter(this, "FileResourcePath", new StringParameterProps
            {
                ParameterName = "/app/origination/api/paths/file",
                StringValue = fileResource.Path,
                Description = "Name of the File Resource in Origination API",
                DataType = ParameterDataType.TEXT,
                Tier = ParameterTier.STANDARD
            });

            // Create SSM parameter for status resource
            new StringParameter(this, "StatusResourcePath", new StringParameterProps
            {
                ParameterName = "/app/origination/api/paths/status",
                StringValue = statusResource.Path,
                Description = "Name of the Status Resource in Origination API",
                DataType = ParameterDataType.TEXT,
                Tier = ParameterTier.STANDARD
            });
            #endregion
        }
    }
}