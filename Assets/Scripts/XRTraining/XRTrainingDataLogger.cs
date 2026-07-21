using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public sealed class XRTrainingDataLogger : MonoBehaviour
{
    [Header("Output")]
    public string outputFolderName = "XRTrainingExperimentData";
    public string customOutputRoot;
    public int maxSavedTrialRecords = 5;

    public float poseSampleIntervalSeconds = 0.1f;
    public Transform headTransform;
    public Transform leftControllerTransform;
    public Transform rightControllerTransform;

    StreamWriter m_EventWriter;
    StreamWriter m_TrajectoryWriter;
    string m_UserId;
    string m_TaskId;
    string m_TrialId;
    string m_Difficulty;
    string m_DifficultyLabel;
    string m_TrialStartedAtUtc;
    int m_TrialNumber;
    float m_PoseAccumulator;

    public string EventFilePath { get; private set; }
    public string TrajectoryFilePath { get; private set; }
    public string SummaryFilePath { get; private set; }
    public string OutputRootPath { get; private set; }
    public bool IsRecording => m_EventWriter != null || m_TrajectoryWriter != null;

    public void ConfigurePoseSources(Transform head, Transform leftController, Transform rightController)
    {
        headTransform = head;
        leftControllerTransform = leftController;
        rightControllerTransform = rightController;
    }

    public void BeginTrial(string userId, string taskId, int trialNumber, XRTrainingDifficulty difficulty, string difficultyLabel)
    {
        EndTrial();

        m_UserId = string.IsNullOrWhiteSpace(userId) ? "P001" : userId;
        m_TaskId = string.IsNullOrWhiteSpace(taskId) ? "ColorBlockTask" : taskId;
        m_TrialNumber = trialNumber;
        m_TrialId = $"{m_TaskId}_{trialNumber:000}";
        m_Difficulty = difficulty.ToString();
        m_DifficultyLabel = difficultyLabel;
        m_PoseAccumulator = 0f;

        DateTime startUtc = DateTime.UtcNow;
        m_TrialStartedAtUtc = startUtc.ToString("o", CultureInfo.InvariantCulture);
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture);
        string root = ResolveOutputRoot();
        OutputRootPath = root;
        string eventDirectory = Path.Combine(root, "Events");
        string trajectoryDirectory = Path.Combine(root, "Trajectories");
        string summaryDirectory = Path.Combine(root, "Summaries");
        Directory.CreateDirectory(eventDirectory);
        Directory.CreateDirectory(trajectoryDirectory);
        Directory.CreateDirectory(summaryDirectory);

        EventFilePath = UniquePath(Path.Combine(eventDirectory, $"{Clean(m_UserId)}_{Clean(m_TaskId)}_{m_TrialNumber:000}_{Clean(m_Difficulty)}_{timestamp}_events.csv"));
        TrajectoryFilePath = UniquePath(Path.Combine(trajectoryDirectory, $"{Clean(m_UserId)}_{Clean(m_TaskId)}_{m_TrialNumber:000}_{Clean(m_Difficulty)}_{timestamp}_trajectory.csv"));
        SummaryFilePath = UniquePath(Path.Combine(summaryDirectory, $"{Clean(m_UserId)}_{Clean(m_TaskId)}_{m_TrialNumber:000}_{Clean(m_Difficulty)}_{timestamp}_summary.csv"));

        m_EventWriter = new StreamWriter(EventFilePath, false, new UTF8Encoding(true));
        m_EventWriter.WriteLine("Timestamp,UserID,TaskID,TrialID,TrialNumber,Difficulty,DifficultyLabel,TaskState,EventType,ObjectName,PositionX,PositionY,PositionZ,ElapsedSeconds,FinalScore,Success,CorrectCount,WrongCount,GrabCount,ReleaseCount,TeleportCount,ResetCount,Details");
        m_EventWriter.Flush();

        m_TrajectoryWriter = new StreamWriter(TrajectoryFilePath, false, new UTF8Encoding(true));
        m_TrajectoryWriter.WriteLine("Timestamp,UserID,TaskID,TrialID,TrialNumber,Difficulty,DifficultyLabel,TaskState,ElapsedSeconds,HeadPosX,HeadPosY,HeadPosZ,HeadRotX,HeadRotY,HeadRotZ,HeadRotW,LeftPosX,LeftPosY,LeftPosZ,LeftRotX,LeftRotY,LeftRotZ,LeftRotW,RightPosX,RightPosY,RightPosZ,RightRotX,RightRotY,RightRotZ,RightRotW");
        m_TrajectoryWriter.Flush();

        PruneOldRecords();
    }

    public void LogEvent(XRTrainingEventType eventType, XRTrainingTaskState taskState, string objectName, Vector3 position, float elapsedSeconds, XRTrainingRuntimeStats stats, string details)
    {
        if (m_EventWriter == null)
            return;

        string[] columns =
        {
            DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            m_UserId,
            m_TaskId,
            m_TrialId,
            m_TrialNumber.ToString(CultureInfo.InvariantCulture),
            m_Difficulty,
            m_DifficultyLabel,
            taskState.ToString(),
            eventType.ToString(),
            objectName ?? string.Empty,
            position.x.ToString("F4", CultureInfo.InvariantCulture),
            position.y.ToString("F4", CultureInfo.InvariantCulture),
            position.z.ToString("F4", CultureInfo.InvariantCulture),
            elapsedSeconds.ToString("F4", CultureInfo.InvariantCulture),
            stats.score.ToString(CultureInfo.InvariantCulture),
            stats.success ? "true" : "false",
            stats.correctPlacements.ToString(CultureInfo.InvariantCulture),
            stats.wrongPlacements.ToString(CultureInfo.InvariantCulture),
            stats.grabCount.ToString(CultureInfo.InvariantCulture),
            stats.releaseCount.ToString(CultureInfo.InvariantCulture),
            stats.teleportCount.ToString(CultureInfo.InvariantCulture),
            stats.resetCount.ToString(CultureInfo.InvariantCulture),
            details ?? string.Empty
        };

        m_EventWriter.WriteLine(CsvLine(columns));
        m_EventWriter.Flush();
    }

    public void WriteTrialSummary(XRTrainingTaskState taskState, XRTrainingRuntimeStats stats, string resultEventType, string details)
    {
        if (string.IsNullOrEmpty(SummaryFilePath))
            return;

        string[] header =
        {
            "StartTimestamp",
            "EndTimestamp",
            "UserID",
            "TaskID",
            "TrialID",
            "TrialNumber",
            "Difficulty",
            "DifficultyLabel",
            "TaskState",
            "ResultEventType",
            "Success",
            "TotalSeconds",
            "FinalScore",
            "CorrectCount",
            "WrongCount",
            "GrabCount",
            "ReleaseCount",
            "TeleportCount",
            "ResetCount",
            "EventFile",
            "TrajectoryFile",
            "Details"
        };

        string[] columns =
        {
            m_TrialStartedAtUtc,
            DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            m_UserId,
            m_TaskId,
            m_TrialId,
            m_TrialNumber.ToString(CultureInfo.InvariantCulture),
            m_Difficulty,
            m_DifficultyLabel,
            taskState.ToString(),
            resultEventType ?? string.Empty,
            stats.success ? "true" : "false",
            stats.elapsedSeconds.ToString("F4", CultureInfo.InvariantCulture),
            stats.score.ToString(CultureInfo.InvariantCulture),
            stats.correctPlacements.ToString(CultureInfo.InvariantCulture),
            stats.wrongPlacements.ToString(CultureInfo.InvariantCulture),
            stats.grabCount.ToString(CultureInfo.InvariantCulture),
            stats.releaseCount.ToString(CultureInfo.InvariantCulture),
            stats.teleportCount.ToString(CultureInfo.InvariantCulture),
            stats.resetCount.ToString(CultureInfo.InvariantCulture),
            EventFilePath ?? string.Empty,
            TrajectoryFilePath ?? string.Empty,
            details ?? string.Empty
        };

        using (var writer = new StreamWriter(SummaryFilePath, false, new UTF8Encoding(true)))
        {
            writer.WriteLine(CsvLine(header));
            writer.WriteLine(CsvLine(columns));
        }

        PruneOldRecords();
    }

    public void CompleteTrial(XRTrainingTaskState taskState, XRTrainingRuntimeStats stats, string resultEventType, string details)
    {
        WriteTrialSummary(taskState, stats, resultEventType, details);
        EndTrial();
    }

    public void TickPoseRecording(XRTrainingTaskState taskState, float elapsedSeconds)
    {
        if (m_TrajectoryWriter == null || taskState != XRTrainingTaskState.Running)
            return;

        m_PoseAccumulator += Time.unscaledDeltaTime;
        if (m_PoseAccumulator < poseSampleIntervalSeconds)
            return;

        while (m_PoseAccumulator >= poseSampleIntervalSeconds)
            m_PoseAccumulator -= poseSampleIntervalSeconds;

        WritePoseSample(taskState, elapsedSeconds);
    }

    public void WritePoseSample(XRTrainingTaskState taskState, float elapsedSeconds)
    {
        if (m_TrajectoryWriter == null)
            return;

        string[] columns =
        {
            DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            m_UserId,
            m_TaskId,
            m_TrialId,
            m_TrialNumber.ToString(CultureInfo.InvariantCulture),
            m_Difficulty,
            m_DifficultyLabel,
            taskState.ToString(),
            elapsedSeconds.ToString("F4", CultureInfo.InvariantCulture),
            Position(headTransform, 0),
            Position(headTransform, 1),
            Position(headTransform, 2),
            Rotation(headTransform, 0),
            Rotation(headTransform, 1),
            Rotation(headTransform, 2),
            Rotation(headTransform, 3),
            Position(leftControllerTransform, 0),
            Position(leftControllerTransform, 1),
            Position(leftControllerTransform, 2),
            Rotation(leftControllerTransform, 0),
            Rotation(leftControllerTransform, 1),
            Rotation(leftControllerTransform, 2),
            Rotation(leftControllerTransform, 3),
            Position(rightControllerTransform, 0),
            Position(rightControllerTransform, 1),
            Position(rightControllerTransform, 2),
            Rotation(rightControllerTransform, 0),
            Rotation(rightControllerTransform, 1),
            Rotation(rightControllerTransform, 2),
            Rotation(rightControllerTransform, 3)
        };

        m_TrajectoryWriter.WriteLine(string.Join(",", columns));
        m_TrajectoryWriter.Flush();
    }

    public void EndTrial()
    {
        if (m_EventWriter != null)
        {
            m_EventWriter.Flush();
            m_EventWriter.Dispose();
            m_EventWriter = null;
        }

        if (m_TrajectoryWriter != null)
        {
            m_TrajectoryWriter.Flush();
            m_TrajectoryWriter.Dispose();
            m_TrajectoryWriter = null;
        }
    }

    void OnDestroy()
    {
        EndTrial();
    }

    public void PruneOldRecords()
    {
        if (maxSavedTrialRecords <= 0)
            return;

        string root = !string.IsNullOrEmpty(OutputRootPath) ? OutputRootPath : ResolveOutputRoot();
        PruneDirectory(Path.Combine(root, "Events"), maxSavedTrialRecords);
        PruneDirectory(Path.Combine(root, "Trajectories"), maxSavedTrialRecords);
        PruneDirectory(Path.Combine(root, "Summaries"), maxSavedTrialRecords);
    }

    void PruneDirectory(string directory, int keepCount)
    {
        if (!Directory.Exists(directory))
            return;

        FileInfo[] files;
        try
        {
            files = new DirectoryInfo(directory).GetFiles("*.csv", SearchOption.TopDirectoryOnly);
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        Array.Sort(files, CompareNewestFirst);

        int retained = 0;
        for (int i = 0; i < files.Length; i++)
        {
            string path = files[i].FullName;
            if (IsCurrentRecordPath(path))
            {
                retained++;
                continue;
            }

            if (retained < keepCount)
            {
                retained++;
                continue;
            }

            TryDeleteFile(path);
        }
    }

    bool IsCurrentRecordPath(string path)
    {
        return SamePath(path, EventFilePath) || SamePath(path, TrajectoryFilePath) || SamePath(path, SummaryFilePath);
    }

    static string Position(Transform source, int axis)
    {
        Vector3 value = source != null ? source.position : Vector3.zero;
        return Axis(value, axis).ToString("F4", CultureInfo.InvariantCulture);
    }

    static string Rotation(Transform source, int axis)
    {
        Quaternion value = source != null ? source.rotation : Quaternion.identity;
        float result = axis == 0 ? value.x : axis == 1 ? value.y : axis == 2 ? value.z : value.w;
        return result.ToString("F4", CultureInfo.InvariantCulture);
    }

    static float Axis(Vector3 value, int axis)
    {
        return axis == 0 ? value.x : axis == 1 ? value.y : value.z;
    }

    static string CsvLine(string[] columns)
    {
        var builder = new StringBuilder();
        for (int i = 0; i < columns.Length; i++)
        {
            if (i > 0)
                builder.Append(',');

            builder.Append(Escape(columns[i]));
        }

        return builder.ToString();
    }

    static string Escape(string value)
    {
        value ??= string.Empty;
        if (!value.Contains(",") && !value.Contains("\"") && !value.Contains("\n") && !value.Contains("\r"))
            return value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    static string Clean(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Unknown";

        var builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            builder.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
        }

        return builder.ToString();
    }

    static int CompareNewestFirst(FileInfo left, FileInfo right)
    {
        int result = right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc);
        return result != 0 ? result : string.CompareOrdinal(left.FullName, right.FullName);
    }

    static bool SamePath(string left, string right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            return false;

        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }

    static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException exception)
        {
            Debug.Log("[XRTrainingDataLogger] Could not delete old record: " + path + " (" + exception.Message + ")");
        }
        catch (UnauthorizedAccessException exception)
        {
            Debug.Log("[XRTrainingDataLogger] Could not delete old record: " + path + " (" + exception.Message + ")");
        }
    }

    string ResolveOutputRoot()
    {
        if (!string.IsNullOrWhiteSpace(customOutputRoot))
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(customOutputRoot));

        string folderName = Clean(outputFolderName);
        if (string.IsNullOrWhiteSpace(folderName))
            folderName = "XRTrainingExperimentData";

#if UNITY_EDITOR
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        if (!string.IsNullOrEmpty(projectRoot))
            return Path.Combine(projectRoot, folderName);
#endif

        string appRoot = Directory.GetParent(Application.dataPath)?.FullName;
        if (!string.IsNullOrEmpty(appRoot))
            return Path.Combine(appRoot, folderName);

        return Path.Combine(Application.dataPath, folderName);
    }

    static string UniquePath(string path)
    {
        if (!File.Exists(path))
            return path;

        string directory = Path.GetDirectoryName(path);
        string name = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        for (int i = 1; i < 1000; i++)
        {
            string candidate = Path.Combine(directory, $"{name}_{i:000}{extension}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(directory, $"{name}_{Guid.NewGuid():N}{extension}");
    }
}
