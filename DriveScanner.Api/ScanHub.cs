using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace DriveScanner.Api.Hubs
{
    public class ScanHub : Hub
    {
        public async Task JoinScanSession(string sessionId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
        }
    }
}
