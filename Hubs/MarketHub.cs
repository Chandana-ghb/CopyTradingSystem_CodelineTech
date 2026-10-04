using Microsoft.AspNetCore.SignalR;

namespace FyersCopyTrading.Hubs
{
    public class MarketHub : Hub
    {
        public async Task JoinSymbol(string symbol)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, symbol);
        }

        public async Task LeaveSymbol(string symbol)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, symbol);
        }
    }
}
