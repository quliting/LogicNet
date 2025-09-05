using LogicNet.Application.UserInfo.DTO;

namespace LogicNet.Application.Hub;

public class UserHub : Microsoft.AspNetCore.SignalR.Hub
{
    public async Task LoginAsync(LoginInputDto inputDto)
    {
        
    }
}