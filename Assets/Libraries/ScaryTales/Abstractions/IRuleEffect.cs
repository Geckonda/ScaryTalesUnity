using ScaryTales.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Assets.Libraries.ScaryTales.Abstractions
{
    public interface IRuleEffect
    {
        public int Id { get; }
        public string Description { get; }
        public bool IsEffectAvailable(IGameContext context);
        public Task<bool> ApplyEffect(IGameContext context);
    }
}
