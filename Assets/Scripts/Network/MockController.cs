using ScaryTales;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Assets.Scripts.Network
{
    public class MockController : INetworkController
    {
        public Task SendAvailableCards(int playerId, List<Card> cards)
        {
            throw new NotImplementedException();
        }

        public Task SendAvailableItems(int playerId, List<Item> items)
        {
            throw new NotImplementedException();
        }

        public Task<int> WaitForCardSelection(int playerId)
        {
            throw new NotImplementedException();
        }

        public Task<int> WaitForItemSelection(int playerId)
        {
            throw new NotImplementedException();
        }

        public Task<bool> WaitForYesOrNo(int playerId)
        {
            throw new NotImplementedException();
        }
    }
}
