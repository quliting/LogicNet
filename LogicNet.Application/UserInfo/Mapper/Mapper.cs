using LogicNet.Core.Security;

namespace LogicNet.Application.UserInfo.DTO;

public class Mapper : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.ForType<AddUserInfoInputDto, Core.Entity.UserInfo>()
            .Map(desc => desc.Password, src => PasswordHasher.Hash(src.PassWord));
    }
}