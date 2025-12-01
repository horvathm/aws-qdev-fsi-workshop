using System.Diagnostics;

namespace Origination.Helpers;

public interface IInstrumentation : IDisposable
{
    ActivitySource ActivitySource { get; }
}
