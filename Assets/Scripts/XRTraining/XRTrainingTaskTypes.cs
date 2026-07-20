using System;
using UnityEngine;

public enum XRTrainingTaskState
{
    WaitingToStart,
    Instructions,
    Running,
    Completed,
    Failed,
    Results,
    Restarting
}

public enum XRTrainingDifficulty
{
    Easy,
    Normal,
    Hard
}

public enum XRTrainingEventType
{
    TaskStart,
    ObjectGrab,
    ObjectRelease,
    CorrectPlacement,
    WrongPlacement,
    Teleport,
    InvalidTeleport,
    TaskComplete,
    TaskFailed,
    TaskReset,
    DifficultyChanged
}

[Serializable]
public sealed class XRTrainingDifficultyConfig
{
    public XRTrainingDifficulty difficulty = XRTrainingDifficulty.Easy;
    public string displayName = "Easy";
    public int blockCount = 3;
    public int distractorCount;
    public float targetRadius = 0.52f;
    public float blockSpacing = 0.78f;
    public float targetSpacing = 1.55f;
    public float targetDistance = 1.75f;
    public float timeLimitSeconds;
    public bool randomizeInitialPositions;
    public float randomRadius = 0.15f;
    public int scorePerCorrect = 100;
    public int penaltyPerWrong = 10;

    public static XRTrainingDifficultyConfig Easy()
    {
        return new XRTrainingDifficultyConfig
        {
            difficulty = XRTrainingDifficulty.Easy,
            displayName = "Easy",
            blockCount = 3,
            distractorCount = 0,
            targetRadius = 0.52f,
            blockSpacing = 0.82f,
            targetSpacing = 1.55f,
            targetDistance = 1.72f,
            timeLimitSeconds = 0f,
            randomizeInitialPositions = false,
            randomRadius = 0.12f,
            scorePerCorrect = 100,
            penaltyPerWrong = 10
        };
    }

    public static XRTrainingDifficultyConfig Normal()
    {
        return new XRTrainingDifficultyConfig
        {
            difficulty = XRTrainingDifficulty.Normal,
            displayName = "Normal",
            blockCount = 5,
            distractorCount = 1,
            targetRadius = 0.43f,
            blockSpacing = 0.76f,
            targetSpacing = 1.35f,
            targetDistance = 2.25f,
            timeLimitSeconds = 120f,
            randomizeInitialPositions = true,
            randomRadius = 0.36f,
            scorePerCorrect = 100,
            penaltyPerWrong = 15
        };
    }

    public static XRTrainingDifficultyConfig Hard()
    {
        return new XRTrainingDifficultyConfig
        {
            difficulty = XRTrainingDifficulty.Hard,
            displayName = "Hard",
            blockCount = 5,
            distractorCount = 2,
            targetRadius = 0.34f,
            blockSpacing = 0.78f,
            targetSpacing = 1.45f,
            targetDistance = 2.65f,
            timeLimitSeconds = 90f,
            randomizeInitialPositions = true,
            randomRadius = 0.52f,
            scorePerCorrect = 100,
            penaltyPerWrong = 20
        };
    }
}

public sealed class XRTrainingRuntimeStats
{
    public int score;
    public int correctPlacements;
    public int wrongPlacements;
    public int grabCount;
    public int releaseCount;
    public int teleportCount;
    public int resetCount;
    public bool success;
    public float elapsedSeconds;

    public void Clear()
    {
        score = 0;
        correctPlacements = 0;
        wrongPlacements = 0;
        grabCount = 0;
        releaseCount = 0;
        teleportCount = 0;
        resetCount = 0;
        success = false;
        elapsedSeconds = 0f;
    }
}
