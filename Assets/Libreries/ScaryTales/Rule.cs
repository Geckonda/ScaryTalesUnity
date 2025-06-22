using Assets.Libreries.ScaryTales.Abstractions;
using Assets.Libreries.ScaryTales.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Assets.Libreries.ScaryTales
{
    public abstract class Rule
    {
        public int Id { get; set; }
        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract RuleType Type { get; }
        public abstract List<IRuleEffect> Effects { get; }
    }
}
