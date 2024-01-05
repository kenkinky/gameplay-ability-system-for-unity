using System.Collections.Generic;
using System.Linq;
using GAS.Runtime.Component;
using GAS.Runtime.Tags;

namespace GAS.Runtime.Ability
{
    public class AbilityContainer
    {
        AbilitySystemComponent _owner;
        private readonly Dictionary<string, AbilitySpec> _abilities = new Dictionary<string, AbilitySpec>();
        
        public void SetOwner(AbilitySystemComponent owner)
        {
            _owner = owner;
        }
        
        public void Tick()
        {
            var enumerable = _abilities.Values.ToArray();
            foreach (var abilitySpec in enumerable)
            {
                abilitySpec.Tick();
            }
        }
        
        public void GrantAbility(AbstractAbility ability)
        {
            if (_abilities.ContainsKey(ability.Name)) return;
            var abilitySpec = ability.CreateSpec(_owner);
            _abilities.Add(ability.Name, abilitySpec);
        }
        
        public void RemoveAbility(AbstractAbility ability)
        {
            RemoveAbility(ability.Name);
        }
        
        public void RemoveAbility(string abilityName)
        {
            if (!_abilities.ContainsKey(abilityName)) return;
            
            EndAbility(abilityName);
            _abilities.Remove(abilityName);
        }
        
        public bool TryActivateAbility(string abilityName, params object[] args)
        {
            if (!_abilities.ContainsKey(abilityName)) return false;
            if (!_abilities[abilityName].TryActivateAbility(args)) return false;
            CancelAbilitiesByTag(_abilities[abilityName].Ability.Tag.CancelAbilitiesWithTags);
            return true;

        }
        
        public void EndAbility(string abilityName)
        {
            if (!_abilities.ContainsKey(abilityName)) return;
            _abilities[abilityName].TryEndAbility();
        }
        
        void CancelAbilitiesByTag(GameplayTagSet tags)
        {
            foreach (var kv in _abilities)
            {
                var abilityTag = kv.Value.Ability.Tag;
                if (abilityTag.AssetTag.HasAnyTags(tags))
                {
                    _abilities[kv.Key].TryCancelAbility();
                }
            }
        }
    }
}