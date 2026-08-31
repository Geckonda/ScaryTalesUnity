using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScaryTales.Enums
{
    public enum CardType
    {
        [Description("Женщина")]
        Woman,
        [Description("Мужчина")]
        Man,
        [Description("Событие")]
        Event,
        [Description("Монстр")]
        Monster,
        [Description("Место")]
        Place
    }

    static class EnumExtensions
    {
        public static string GetDescription(this Enum value)
        {
            var field = value.GetType().GetField(value.ToString());
            var attribute = (DescriptionAttribute)Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute));
            return attribute == null ? value.ToString() : attribute.Description;
        }
    }
}
