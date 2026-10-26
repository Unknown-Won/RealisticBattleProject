// TroopHealthBar.UnitStatusVM
using RBM.AgentStatusBar;
using RBMAI;
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;

namespace RBM.AgentStatusBar
{
    public class UnitStatusVM : ViewModel
    {
        private Dictionary<Agent, AgentStatusVM> _agentStatusMap;

        private MBBindingList<AgentStatusVM> _agentStatusList;

        private Camera _camera;

        private Agent _mainAgent;

        private bool _escapeMenuOpened = false;

        private bool _keyToggled = true;

        private readonly List<KeyValuePair<Agent, AgentStatusVM>> _entriesToRemove = new List<KeyValuePair<Agent, AgentStatusVM>>();

        // Share of the screen size a bar's anchor may be outside the screen and still be drawn (bars extend past their anchor).
        private const float OffScreenMargin = 0.1f;

        // Set once per frame while bars are shown, read for every agent.
        private Vec3 _cameraDirection;

        private float _screenWidth, _screenHeight, _screenMarginX, _screenMarginY;

        private List<Agent> _newAgents = new List<Agent>();

        [DataSourceProperty]
        public MBBindingList<AgentStatusVM> AgentStatus => _agentStatusList;

        public UnitStatusVM(Camera camera)
        {
            _camera = camera;
            _agentStatusMap = new Dictionary<Agent, AgentStatusVM>();
            _agentStatusList = new MBBindingList<AgentStatusVM>();
        }

        public override void OnFinalize()
        {
            _agentStatusMap.ToList().ForEach(delegate (KeyValuePair<Agent, AgentStatusVM> entry)
            {
                entry.Value.OnFinalize();
            });
            _agentStatusMap = null;
            _camera = null;
            _mainAgent = null;
            base.OnFinalize();
        }

        public void ToggleVisibility()
        {
            _keyToggled = !_keyToggled;
        }

        public void Tick(float dt)
        {
            RefreshAgentStatus(dt);
        }

        private void RefreshAgentStatus(float dt)
        {
            if (_newAgents.Count > 0)
            {
                foreach (Agent agent in _newAgents)
                {
                    if (!agent.IsMainAgent && !agent.IsMount && agent.Team != null)
                    {
                        AddAgentToMap(agent);
                    }
                }
                _newAgents.Clear();
            }
            // Only bars that can show (not toggled off, hit, focused or within DistanceCutOff) are projected to the screen and
            // updated; all others are just hidden. The map isn't changed inside the loop: removals are applied after it.
            bool barsShown = !_keyToggled && !_escapeMenuOpened;
            Vec3 playerPos = _mainAgent != null ? _mainAgent.Position : _camera.Position;
            Vec3 cameraPos = _camera.Position;
            float cutOffSquared = (float)AgentStatusBarConfig.DistanceCutOff * AgentStatusBarConfig.DistanceCutOff;
            if (barsShown)
            {
                _cameraDirection = _camera.Direction;
                _screenMarginX = Screen.RealScreenResolutionWidth * OffScreenMargin;
                _screenMarginY = Screen.RealScreenResolutionHeight * OffScreenMargin;
                _screenWidth = Screen.RealScreenResolutionWidth;
                _screenHeight = Screen.RealScreenResolutionHeight;
            }
            foreach (KeyValuePair<Agent, AgentStatusVM> entry2 in _agentStatusMap)
            {
                // Never read a deleted agent's position (freed native pointer); the entry is dropped below.
                if (entry2.Key.State == AgentState.Deleted)
                {
                    _entriesToRemove.Add(entry2);
                    continue;
                }
                RefreshEntry(entry2.Key, entry2.Value, dt, barsShown, playerPos, cameraPos, cutOffSquared);
                if (entry2.Value._isRemoved && !entry2.Value._isHit)
                {
                    _entriesToRemove.Add(entry2);
                }
            }
            for (int i = 0; i < _entriesToRemove.Count; i++)
            {
                RemoveEntry(_entriesToRemove[i].Key, _entriesToRemove[i].Value);
            }
            _entriesToRemove.Clear();
        }

        private void RefreshEntry(Agent agent, AgentStatusVM status, float dt, bool barsShown, Vec3 playerPos, Vec3 cameraPos, float cutOffSquared)
        {
            if (!status.IsEnabled || !barsShown)
            {
                status.IsHidden = true;
                return;
            }
            Vec3 pos = status._agent.Position;
            pos.z += (!agent.HasMount) ? 2.2f : 3.2f;
            if (!status._isHit && !status.IsAgentFocused && (pos - playerPos).LengthSquared > cutOffSquared)
            {
                status.IsHidden = true;
                return;
            }
            // Behind the camera the projection below would give w < 0, which hides the bar anyway.
            if (Vec3.DotProduct(pos - cameraPos, _cameraDirection) < 0f)
            {
                status.IsHidden = true;
                return;
            }
            float num0 = 0f;
            float num1 = 0f;
            float num2 = 0f;
            MBWindowManager.WorldToScreen(_camera, pos, ref num0, ref num1, ref num2);
            float distance = pos.Distance(playerPos);
            if (num2 >= 0f && (status._isHit || distance <= (float)AgentStatusBarConfig.DistanceCutOff || status.IsAgentFocused))
            {
                // Off screen: nothing to draw, but the hit fade keeps running so the bar still times out and gets removed.
                if (num0 < -_screenMarginX || num0 > _screenWidth + _screenMarginX || num1 < -_screenMarginY || num1 > _screenHeight + _screenMarginY)
                {
                    status.IsHidden = true;
                    status.tick(dt, _keyToggled);
                    return;
                }
                status.Distance = distance;
                status.IsHidden = false;
                status.ScreenZ = Convert.ToInt32(num2);
                status.ScreenDistance = pos.Distance(cameraPos);
                status.ScreenXPosition = num0;
                status.ScreenYPosition = num1;
                status.tick(dt, _keyToggled);
                if (RBMConfig.RBMConfig.rbmAiEnabled && RBMConfig.RBMConfig.postureEnabled && AgentStances.values.TryGetValue(agent, out Stance stanceData))
                {
                    status.ShowStance = true;
                    status.Posture = Convert.ToInt32(stanceData.posture);
                    status.MaxPosture = Convert.ToInt32(stanceData.maxPosture);
                    status.Stamina = Convert.ToInt32(stanceData.stamina);
                    status.MaxStamina = Convert.ToInt32(stanceData.maxStamina);
                }
                else
                {
                    status.ShowStance = false;
                }
            }
            else
            {
                status.IsHidden = true;
            }
        }

        private void RemoveEntry(Agent agent, AgentStatusVM status)
        {
            status.IsEnabled = false;
            _agentStatusMap.Remove(agent);
            _agentStatusList.Remove(status);
        }

        public void OnAgentDeleted(Agent agent)
        {
            if (_agentStatusMap != null && _agentStatusMap.TryGetValue(agent, out AgentStatusVM status))
            {
                RemoveEntry(agent, status);
            }
            _newAgents.Remove(agent);
        }

        public void OnAgentHit(Agent agent, bool isMainAgent)
        {
            if (_agentStatusMap.ContainsKey(agent))
            {
                _agentStatusMap[agent].OnAgentHit(isMainAgent);
            }
        }

        public void OnFocusGained(Agent agent)
        {
            if (_agentStatusMap.ContainsKey(agent))
            {
                _agentStatusMap[agent].OnFocusGained();
            }
            else if (agent.IsMount && agent.RiderAgent != null && _agentStatusMap.ContainsKey(agent.RiderAgent))
            {
                _agentStatusMap[agent.RiderAgent].OnFocusGained();
            }
        }

        public void OnFocusLost(Agent agent)
        {
            if (_agentStatusMap.ContainsKey(agent))
            {
                _agentStatusMap[agent].OnFocusLost();
            }
            else if (agent.IsMount && agent.RiderAgent != null && _agentStatusMap.ContainsKey(agent.RiderAgent))
            {
                _agentStatusMap[agent.RiderAgent].OnFocusLost();
            }
        }

        public void OnAgentRemoved(Agent agent, bool isMainAgent)
        {
            if (_agentStatusMap.ContainsKey(agent))
            {
                _agentStatusMap[agent].OnAgentRemoved(isMainAgent);
            }
            else if (agent.IsMount && agent.RiderAgent != null && _agentStatusMap.ContainsKey(agent.RiderAgent))
            {
                _agentStatusMap[agent.RiderAgent].OnAgentRemoved(isMainAgent);
            }
        }

        public void OnAgentCreated(Agent agent)
        {
            if (agent.Team != null)
            {
                AddAgentToMap(agent);
            }
            else
            {
                _newAgents.Add(agent);
            }
        }

        private void AddAgentToMap(Agent agent)
        {
            if (!_agentStatusMap.ContainsKey(agent) && !agent.IsMainAgent && ((AgentStatusBarConfig.AllyHealthBars && agent.Team.IsPlayerAlly) || (AgentStatusBarConfig.EnemyHealthBars && !agent.Team.IsPlayerAlly) || (AgentStatusBarConfig.HeroHealthBars && agent.IsHero)))
            {
                AgentStatusVM asvm = new AgentStatusVM(agent);
                _agentStatusMap.Add(agent, asvm);
                _agentStatusList.Add(asvm);
            }
        }
    }
}