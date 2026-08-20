using System.Runtime.Serialization;

namespace Eq2Auras.Core.Timers
{
    /// A rule type over a timer reading's raw attributes. Panel is the 0-value so a
    /// DCJS-deserialized rule with an absent type lands on it (the migration default).
    public enum SourceRuleType { Panel = 0, Category = 1, Name = 2 }

    /// One clause of a group's source: "match readings whose {Type} equals {Value}".
    /// A group's source is a LIST of these (union). This is the whole association model's
    /// data — routing lives on the window, not on the timer (SPEC §Timer groups).
    [DataContract]
    public sealed class SourceRule
    {
        [DataMember(Name = "type")]
        public SourceRuleType Type { get; set; }

        [DataMember(Name = "value")]
        public string Value { get; set; }

        public static SourceRule Panel(int n) => new SourceRule { Type = SourceRuleType.Panel, Value = n.ToString() };
        public static SourceRule OfCategory(string c) => new SourceRule { Type = SourceRuleType.Category, Value = c };
        public static SourceRule OfName(string n) => new SourceRule { Type = SourceRuleType.Name, Value = n };
    }
}
