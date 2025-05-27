namespace LogicNet.Application;

public class LogicBase : IDynamicApiController, IScoped
{
    public LogicBase()
    {
        StaticConfig.CustomSnowFlakeFunc = YitIdHelper.NextId;
    }
}