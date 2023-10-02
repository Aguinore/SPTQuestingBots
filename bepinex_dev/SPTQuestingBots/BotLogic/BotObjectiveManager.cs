﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EFT;
using SPTQuestingBots.Controllers;
using UnityEngine;

namespace SPTQuestingBots.BotLogic
{
    internal class BotObjectiveManager : MonoBehaviourDelayedUpdate
    {
        public bool IsObjectiveActive { get; private set; } = false;
        public bool IsObjectiveReached { get; private set; } = false;
        public bool CanChangeObjective { get; set; } = true;
        public bool CanRushPlayerSpawn { get; private set; } = false;
        public bool CanReachObjective { get; private set; } = true;
        public bool IsObjectivePathComplete { get; set; } = true;
        public int StuckCount { get; set; } = 0;
        public float MinTimeAtObjective { get; set; } = 10f;
        public Vector3? Position { get; set; } = null;

        private BotOwner botOwner = null;
        private Models.Quest targetQuest = null;
        private Models.QuestObjective targetObjective = null;
        private Models.QuestObjectiveStep targetObjectiveStep = null;
        private Stopwatch timeSpentAtObjectiveTimer = new Stopwatch();
        private Stopwatch timeSinceChangingObjectiveTimer = Stopwatch.StartNew();

        public double TimeSpentAtObjective
        {
            get { return timeSpentAtObjectiveTimer.ElapsedMilliseconds / 1000.0; }
        }

        public double TimeSinceChangingObjective
        {
            get { return timeSinceChangingObjectiveTimer.ElapsedMilliseconds / 1000.0; }
        }

        public float DistanceToObjective
        {
            get { return Position.HasValue ? Vector3.Distance(Position.Value, botOwner.Position) : float.NaN; }
        }

        public void Init(BotOwner _botOwner)
        {
            base.UpdateInterval = 200;
            botOwner = _botOwner;

            BotType botType = BotQuestController.GetBotType(botOwner);

            if ((botType == BotType.PMC) && ConfigController.Config.AllowedBotTypesForQuesting.PMC)
            {
                CanRushPlayerSpawn = BotGenerator.IsBotFromInitialPMCSpawns(botOwner);
                IsObjectiveActive = true;
            }
            if ((botType == BotType.Boss) && ConfigController.Config.AllowedBotTypesForQuesting.Boss)
            {
                IsObjectiveActive = true;
            }
            if ((botType == BotType.Scav) && ConfigController.Config.AllowedBotTypesForQuesting.Scav)
            {
                IsObjectiveActive = true;
            }

            if (IsObjectiveActive && BotQuestController.HaveTriggersBeenFound)
            {
                LoggingController.LogInfo("Setting objective for " + botType.ToString() + " " + botOwner.Profile.Nickname + " (Brain type: " + botOwner.Brain.BaseBrain.ShortName() + ")");
                TryChangeObjective();
            }

            if (botType == BotType.Undetermined)
            {
                LoggingController.LogError("Could not determine bot type for " + botOwner.Profile.Nickname + " (Brain type: " + botOwner.Brain.BaseBrain.ShortName() + ")");
            }
        }

        private void Update()
        {
            if (!BotQuestController.HaveTriggersBeenFound)
            {
                return;
            }

            if (!IsObjectiveActive)
            {
                return;
            }

            if (IsObjectiveReached)
            {
                timeSpentAtObjectiveTimer.Start();
            }
            else
            {
                timeSpentAtObjectiveTimer.Reset();
            }

            if (!canUpdate())
            {
                return;
            }

            if (!Position.HasValue)
            {
                TryChangeObjective();
            }
        }

        public void CompleteObjective()
        {
            IsObjectivePathComplete = true;
            IsObjectiveReached = true;
            targetObjective.BotCompletedObjective(botOwner);
            targetQuest.CompleteObjective(botOwner, targetObjective);
            targetQuest.StartQuestForBot(botOwner);
            StuckCount = 0;
        }

        public void RejectObjective()
        {
            IsObjectivePathComplete = true;
            CanReachObjective = false;
            targetObjective.BotFailedObjective(botOwner);
        }

        public void StopQuesting()
        {
            IsObjectiveActive = false;
            LoggingController.LogInfo("Bot " + botOwner.Profile.Nickname + " is no longer allowed to quest.");
        }

        public bool IsCloseToObjective()
        {
            return IsCloseToObjective(ConfigController.Config.BotSearchDistances.OjectiveReachedIdeal);
        }

        public bool IsCloseToObjective(float distance)
        {
            return DistanceToObjective <= distance;
        }

        public bool CanSprintToObjective()
        {
            if ((targetObjective != null) && (DistanceToObjective < targetObjective.MaxRunDistance))
            {
                //LoggingController.LogInfo("Bot " + botOwner.Profile.Nickname + " will stop running because it's too close to " + targetObjective.ToString());
                return false;
            }

            if ((targetQuest != null) && targetQuest.HasBotCompletedAnyObjectives(botOwner) && !targetQuest.CanRunBetweenObjectives)
            {
                //LoggingController.LogInfo("Bot " + botOwner.Profile.Nickname + " can no longer run for quest " + targetQuest.Name);
                return false;
            }

            return true;
        }

        public override string ToString()
        {
            if (targetQuest != null)
            {
                return (targetObjective?.ToString() ?? "???") + " for quest " + targetQuest.Name;
            }

            return "Position " + (Position?.ToString() ?? "???");
        }

        public bool TryChangeObjective()
        {
            if (!CanChangeObjective)
            {
                return false;
            }

            if (TryPerformNextQuestObjectiveStep())
            {
                LoggingController.LogInfo("Bot " + botOwner.Profile.Nickname + " is performing the next step for " + targetObjective.ToString());
                return true;
            }

            if (TryToGoToRandomQuestObjective())
            {
                LoggingController.LogInfo("Bot " + botOwner.Profile.Nickname + " has accepted objective " + ToString());
                return true;
            }

            //LoggingController.LogWarning("Could not assign quest for bot " + botOwner.Profile.Nickname);
            return false;
        }

        private bool TryPerformNextQuestObjectiveStep()
        {
            targetObjectiveStep = targetObjective?.GetNextObjectiveStep(botOwner);

            if (targetObjectiveStep == null)
            {
                return false;
            }

            return true;
        }

        private bool TryToGoToRandomQuestObjective()
        {
            while (targetQuest == null)
            {
                targetQuest = BotQuestController.GetRandomQuestForBot(botOwner);

                if (targetQuest == null)
                {
                    LoggingController.LogWarning("Could not find a quest for bot " + botOwner.Profile.Nickname);
                    return false;
                }

                if (targetQuest.GetRemainingObjectiveCount(botOwner) == 0)
                {
                    //targetQuest.BlacklistBot(botOwner);
                    targetQuest = null;
                }
            }

            Models.QuestObjective nextObjective = targetQuest.GetRandomNewObjective(botOwner);
            if (nextObjective == null)
            {
                LoggingController.LogWarning("Could not find another objective for bot " + botOwner.Profile.Nickname + " for quest " + targetQuest.Name);
                //targetQuest.BlacklistBot(botOwner);
                targetQuest.StopQuestForBot(botOwner);
                targetQuest = null;
                return false;
            }

            if (!nextObjective.TryAssignBot(botOwner))
            {
                LoggingController.LogWarning("Bot " + botOwner.Profile.Nickname + " cannot be assigned to " + ToString() + ". Too many bots already assigned to it.");
                return false;
            }

            targetObjectiveStep = nextObjective.GetNextObjectiveStep(botOwner);
            Vector3? nextObjectiveStepPosition = targetObjectiveStep?.GetPosition();

            if (targetObjectiveStep == null)
            {
                LoggingController.LogInfo("Bot " + botOwner.Profile.Nickname + " has completed all steps for " + ToString());
                nextObjective.BotCompletedObjective(botOwner);
                return false;
            }

            if (!nextObjectiveStepPosition.HasValue)
            {
                LoggingController.LogWarning("Bot " + botOwner.Profile.Nickname + " cannot be assigned to " + ToString() + targetQuest.Name + ". Invalid position.");
                nextObjective.BotFailedObjective(botOwner);
                return false;
            }

            targetObjective = nextObjective;
            updateObjective(nextObjectiveStepPosition.Value);
            
            return true;
        }

        private void updateObjective(Vector3 newTargetPosition)
        {
            Position = newTargetPosition;
            IsObjectiveReached = false;
            CanReachObjective = true;
            timeSinceChangingObjectiveTimer.Restart();
        }
    }
}
