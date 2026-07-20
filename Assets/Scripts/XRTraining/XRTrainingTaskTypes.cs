using System;

public enum XRTrainingTaskState
{
    WaitingToStart,
    Running,
    Completed,
    Ended
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
    ObjectSelected,
    CorrectPlacement,
    WrongPlacement,
    Teleport,
    InvalidTeleport,
    TaskComplete,
    TaskEnded,
    TaskReset,
    LightToggled
}

[Serializable]
public sealed class XRTrainingDifficultyConfig
{
    public XRTrainingDifficulty difficulty = XRTrainingDifficulty.Easy;
    public string displayName = "Basic";
    public int blockCount = 3;
    public int scorePerCorrect = 1;
    public int penaltyPerWrong = 0;

    public static XRTrainingDifficultyConfig Easy()
    {
        return new XRTrainingDifficultyConfig();
    }

    public static XRTrainingDifficultyConfig Normal()
    {
        return new XRTrainingDifficultyConfig { difficulty = XRTrainingDifficulty.Normal, displayName = "Normal" };
    }

    public static XRTrainingDifficultyConfig Hard()
    {
        return new XRTrainingDifficultyConfig { difficulty = XRTrainingDifficulty.Hard, displayName = "Hard" };
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
