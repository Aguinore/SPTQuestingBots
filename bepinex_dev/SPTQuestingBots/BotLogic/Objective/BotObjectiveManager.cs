﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EFT;
using SPTQuestingBots.Controllers;
using SPTQuestingBots.Controllers.Bots;
using SPTQuestingBots.Models;
using UnityEngine;

namespace SPTQuestingBots.BotLogic.Objective
{
    internal class BotObjectiveManager : BehaviorExtensions.MonoBehaviourDelayedUpdate
    {
        public bool IsInitialized { get; private set; } = false;
        public bool IsQuestingAllowed { get; private set; } = false;
        public bool CanRushPlayerSpawn { get; private set; } = false;
        public int StuckCount { get; set; } = 0;
        public float MinTimeAtObjective { get; set; } = 10f;
        public BotMonitor BotMonitor { get; private set; } = null;

        private BotOwner botOwner = null;
        private BotJobAssignment assignment = null;
        private Stopwatch timeSpentAtObjectiveTimer = new Stopwatch();
        private Stopwatch timeSinceInitializationTimer = new Stopwatch();

        public Vector3? Position => assignment?.Position;
        public bool IsJobAssignmentActive => assignment?.IsActive == true;

        public double TimeSpentAtObjective
        {
            get { return timeSpentAtObjectiveTimer.ElapsedMilliseconds / 1000.0; }
        }

        public double TimeSinceInitialization
        {
            get { return timeSinceInitializationTimer.ElapsedMilliseconds / 1000.0; }
        }

        public float DistanceToObjective
        {
            get { return Position.HasValue ? Vector3.Distance(Position.Value, botOwner.Position) : float.NaN; }
        }

        public static BotObjectiveManager GetObjectiveManagerForBot(BotOwner bot)
        {
            if (bot == null)
            {
                return null;
            }

            if (!bot.isActiveAndEnabled || bot.IsDead)
            {
                return null;
            }

            Player botPlayer = bot.GetPlayer;
            if (botPlayer == null)
            {
                return null;
            }

            GameObject obj = botPlayer.gameObject;
            if (obj == null)
            {
                return null;
            }

            if (obj.TryGetComponent(out BotObjectiveManager objectiveManager))
            {
                return objectiveManager;
            }

            return null;
        }

        public void Init(BotOwner _botOwner)
        {
            base.UpdateInterval = 200;
            botOwner = _botOwner;

            BotMonitor = new BotMonitor(botOwner);
        }

        private void updateBotType()
        {
            if (!HiveMind.BotHiveMindMonitor.IsRegistered(botOwner))
            {
                return;
            }

            BotType botType = BotRegistrationManager.GetBotType(botOwner);

            if ((botType == BotType.PMC) && ConfigController.Config.AllowedBotTypesForQuesting.PMC)
            {
                CanRushPlayerSpawn = BotGenerator.IsBotFromInitialPMCSpawns(botOwner);
                IsQuestingAllowed = true;
            }
            if ((botType == BotType.Boss) && ConfigController.Config.AllowedBotTypesForQuesting.Boss)
            {
                IsQuestingAllowed = true;
            }
            if ((botType == BotType.Scav) && ConfigController.Config.AllowedBotTypesForQuesting.Scav)
            {
                IsQuestingAllowed = true;
            }

            // Only set an objective for the bot if its type is allowed to spawn and all quests have been loaded and generated
            if (IsQuestingAllowed && BotQuestBuilder.HaveQuestsBeenBuilt)
            {
                LoggingController.LogInfo("Setting objective for " + botType.ToString() + " " + botOwner.Profile.Nickname + " (Brain type: " + botOwner.Brain.BaseBrain.ShortName() + ")");
                assignment = botOwner.GetCurrentJobAssignment();
            }

            if (botType == BotType.Undetermined)
            {
                LoggingController.LogError("Could not determine bot type for " + botOwner.Profile.Nickname + " (Brain type: " + botOwner.Brain.BaseBrain.ShortName() + ")");
                return;
            }

            timeSinceInitializationTimer.Start();
            IsInitialized = true;
        }

        private void Update()
        {
            if (!BotQuestBuilder.HaveQuestsBeenBuilt)
            {
                return;
            }

            if (!IsInitialized)
            {
                updateBotType();
                return;
            }

            if (!IsQuestingAllowed)
            {
                return;
            }

            if (IsCloseToObjective())
            {
                timeSpentAtObjectiveTimer.Start();
            }
            else
            {
                timeSpentAtObjectiveTimer.Reset();
            }

            // Don't allow expensive parts of this behavior (selecting an objective) to run too often
            if (!canUpdate())
            {
                return;
            }

            if ((assignment == null) || assignment.HasWaitedLongEnoughAfterEnding)
            {
                assignment = botOwner.GetCurrentJobAssignment();
            }
        }

        public void CompleteObjective()
        {
            assignment.CompleteJobAssingment();

            StuckCount = 0;
        }

        public void FailObjective()
        {
            assignment.FailJobAssingment();

            TryChangeObjective();
        }

        public bool TryChangeObjective()
        {
            if (assignment?.TimeSinceJobEnded < ConfigController.Config.MinTimeBetweenSwitchingObjectives)
            {
                return false;
            }

            assignment = botOwner.GetNewBotJobAssignment();
            //LoggingController.LogInfo("Bot " + botOwner.GetText() + " is now doing " + assignment.ToString());

            return true;
        }

        public void StartJobAssigment()
        {
            assignment.StartJobAssignment();
        }

        public void StopQuesting()
        {
            IsQuestingAllowed = false;
            LoggingController.LogInfo(botOwner.GetText() + " is no longer allowed to quest.");
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
            if (assignment == null)
            {
                return true;
            }

            if ((assignment.QuestObjectiveAssignment != null) && (DistanceToObjective < assignment.QuestObjectiveAssignment.MaxRunDistance))
            {
                //LoggingController.LogInfo("Bot " + botOwner.Profile.Nickname + " will stop running because it's too close to " + targetObjective.ToString());
                return false;
            }

            if 
            (
                (assignment.QuestAssignment != null)
                && !assignment.QuestAssignment.CanRunBetweenObjectives
                && (assignment.QuestAssignment.TimeSinceLastObjectiveEndedForBot(botOwner) > 0)
            )
            {
                //LoggingController.LogInfo("Bot " + botOwner.Profile.Nickname + " can no longer run for quest " + targetQuest.Name);
                return false;
            }

            return true;
        }

        public override string ToString()
        {
            if (assignment.QuestAssignment != null)
            {
                return (assignment.QuestObjectiveAssignment?.ToString() ?? "???") + " for quest " + assignment.QuestAssignment.Name;
            }

            return "Position " + (Position?.ToString() ?? "???");
        }
    }
}
