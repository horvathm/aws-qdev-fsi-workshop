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

namespace ApplySampleCdk
{
    public class ApplySampleCdkStack : Stack
    {
        internal ApplySampleCdkStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
        {
            #region infrastructure
            // First get the VPC lookup
            var labVpc = Vpc.FromLookup(this, "LabVpc", new VpcLookupOptions
            {
                VpcName = "microsvc-workshop-VPC",
                SubnetGroupNameTag = "aws-cdk:subnet-type"
            });

            // New security group called ApplicationSG in the labVpc that allows outbound traffic
            SecurityGroup applicationSG = new SecurityGroup(this, "ApplicationWebSG", new SecurityGroupProps
            {
                Vpc = labVpc,
                AllowAllOutbound = true
            });
            #endregion

            #region Fargate


            // Create the Fargate Cluster in the vpc, name it ApplicationCluster
            Cluster applicationWebCluster = new Cluster(this, "ApplicationWebCluster", new ClusterProps
            {
                Vpc = labVpc
            });

            // Create CloudWatch LogGroup called ApplicationSvcLogs, log group name is origination-logs. Set retention to 3 days
            LogGroup applicationWebSvcLogs = new LogGroup(this, "ApplicationWebSvcLogs", new LogGroupProps
            {
                LogGroupName = "/application-web-logs",
                Retention = RetentionDays.THREE_DAYS,
                RemovalPolicy = RemovalPolicy.DESTROY
            });

            // Service task definition ApplicationWebTaskDefn for OriginationSvc
            // Use FARGATE, set cpu = 512 and memory = 1024
            // Use Linux on ARM64
            // For the TaskRole, look up existing IAM role called ApplicationWebTaskRole
            TaskDefinition applicationWebTaskDefn = new TaskDefinition(this, "ApplicationWebTaskDefn", new TaskDefinitionProps
            {
                Family = "ApplicationWeb",
                Compatibility = Compatibility.FARGATE,
                Cpu = "1024",
                MemoryMiB = "6144",
                TaskRole = Role.FromRoleName(this, "EcsTaskRole", "WorkshopApplicationWebTaskRole"),
                ExecutionRole = Role.FromRoleName(this, "EcsTaskExecutionRole", "WorkshopEcsTaskExecutionRole"),
                RuntimePlatform = new RuntimePlatform
                {
                    OperatingSystemFamily = OperatingSystemFamily.LINUX,
                    CpuArchitecture = CpuArchitecture.ARM64
                },
            });

            // Build the Docker Image ApplicationWebAsset to use with Amazon ECR, target Linux on ARM64 
            // Dockerfile path would be in "/workshop-folder/ApplySample/src/ApplySample"
            var applicationWebAsset = new Amazon.CDK.AWS.Ecr.Assets.DockerImageAsset(this, "ApplicationWebAsset", new DockerImageAssetProps
            {
                Directory = Path.GetFullPath("/workshop-folder/ApplySample/src/ApplySample"),
                Platform = Platform_.LINUX_ARM64
            });

            // Add the container into the task definition 
            // Set memory limit to 1024 and mark this as essential container
            // Use the log group applicationWebSvcLogs, set the stream prefix to webapp-logs
            // Set the following environment variables: 
            //   {"ASPNETCORE_URLS", "http://+:7789"},
            //   { "ASPNETCORE_ENVIRONMENT", "Development"}
            // Define health check using CMD wget --no-verbose --tries=1 --spider http://localhost:6268/hc || exit 1
            // Health check internal is 30s, timeout 10s, start period 30s, retry up to 3 times
            // Map port 7789 to container
            ContainerDefinition applicationWebContainer = applicationWebTaskDefn.AddContainer("ApplicationWebContainer", new ContainerDefinitionOptions
            {
                Image = ContainerImage.FromDockerImageAsset(applicationWebAsset),
                MemoryLimitMiB = 6144,
                Essential = true,
                Logging = new AwsLogDriver(new AwsLogDriverProps
                {
                    LogGroup = applicationWebSvcLogs,
                    StreamPrefix = "webapp-logs"
                }),
                Environment = new Dictionary<string, string>() {
                    {"ASPNETCORE_URLS", "http://+:7789"},
                    {"ASPNETCORE_ENVIRONMENT", "Development"},
                    {"AWS_REGION", Of(this).Region },
                },
                HealthCheck = new Amazon.CDK.AWS.ECS.HealthCheck
                {
                    Command = new[] {
                        "CMD-SHELL",
                        "wget --no-verbose --tries=1 --spider http://localhost:7789/hc -O /dev/null 2>&1 || exit 1"
                        },
                    Interval = Duration.Seconds(30),
                    Timeout = Duration.Seconds(10),
                    Retries = 3,
                    StartPeriod = Duration.Seconds(60)
                },
                PortMappings = new[] { new PortMapping { ContainerPort = 7789 } }
            });

            // Create the ECS Fargate Service using the ECS cluster, task definition and security group defined above
            // Set desired count to 2, deploy them onto private subnets one per AZ and ensure all containers are healthy
            FargateService ecsFargateService = new FargateService(this, "EcsFargateService", new FargateServiceProps
            {
                Cluster = applicationWebCluster,
                DesiredCount = 1,
                TaskDefinition = applicationWebTaskDefn,
                SecurityGroups = new[] { applicationSG },
                MaxHealthyPercent = 200,
                MinHealthyPercent = 100,
                VpcSubnets = new SubnetSelection
                {
                    SubnetType = SubnetType.PRIVATE_WITH_EGRESS,
                    OnePerAz = true
                }
            });
            #endregion

            #region ALB
            ApplicationLoadBalancer alb = new ApplicationLoadBalancer(this, "ALB", new ApplicationLoadBalancerProps
            {
                Vpc = labVpc,
                InternetFacing = true
            });

            ApplicationListener listener = alb.AddListener("PublicListener", new BaseApplicationListenerProps { Port = 80 });

            // Attach ALB to ECS Service
            listener.AddTargets("WebApp", new AddApplicationTargetsProps
            {
                Port = 80,
                Targets = new[] { ecsFargateService.LoadBalancerTarget( new LoadBalancerTargetOptions {
                    ContainerName = "ApplicationWebContainer",
                    ContainerPort = 7789
                })},
                HealthCheck = new Amazon.CDK.AWS.ElasticLoadBalancingV2.HealthCheck
                {
                    HealthyThresholdCount = 2,
                    Interval = Duration.Seconds(5),
                    Timeout = Duration.Seconds(2),
                    Path = "/hc"
                },
                // Only drain containers for 10 seconds when stopping them.
                // Increase if your app has long lived connections
                DeregistrationDelay = Duration.Seconds(10)
            });

            //Output the DNS where you can access your service
            new CfnOutput(this, "WebApp-LoadBalancerURL", new CfnOutputProps
            { Value = "http://" + alb.LoadBalancerDnsName });
            #endregion

        }
    }
}