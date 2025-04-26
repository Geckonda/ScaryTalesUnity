using Assets.Scripts.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ScaryTales;

namespace Assets.Scripts.Utilities
{
    public static class ConverterDTO
    {
        public static PlayerDTO PlayerToDTO(Player player, bool isStartPlayer)
        {
            return new PlayerDTO(player.Id, player.Name, isStartPlayer);
        }
    }
}
