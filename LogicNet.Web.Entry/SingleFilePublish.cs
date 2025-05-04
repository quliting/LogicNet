using Furion;
using System.Reflection;

namespace LogicNet.Web.Entry;

public class SingleFilePublish : ISingleFilePublish
{
    public Assembly[] IncludeAssemblies()
    {
        return Array.Empty<Assembly>();
    }

    public string[] IncludeAssemblyNames()
    {
        return new[]
        {
            "LogicNet.Application",
            "LogicNet.Core",
            "LogicNet.Web.Core"
        };
    }
}