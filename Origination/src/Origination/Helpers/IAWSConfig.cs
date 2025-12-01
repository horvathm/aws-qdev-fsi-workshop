namespace Origination.Helpers
{
    public interface IAWSConfig
    {
        string GetStringFromSSM(string parameterName);
    }
}
