using System;
using System.IO;
using System.Reflection;
using UnityEditor;

[InitializeOnLoad]
public static class CodexBuildEnvironment
{
    const string TempRoot = @"D:\codex_tmp";
    static int unitySkillsStartAttempts;
    static double nextUnitySkillsStartAttempt;

    static CodexBuildEnvironment()
    {
        SetDirectoryEnv("TEMP", TempRoot);
        SetDirectoryEnv("TMP", TempRoot);
        SetDirectoryEnv("GRADLE_USER_HOME", Path.Combine(TempRoot, "gradle"));
        SetDirectoryEnv("ANDROID_USER_HOME", Path.Combine(TempRoot, ".android"));
        Environment.SetEnvironmentVariable("JAVA_TOOL_OPTIONS", "-Duser.home=D:\\codex_tmp", EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("ANDROID_SDK_HOME", null, EnvironmentVariableTarget.Process);
        nextUnitySkillsStartAttempt = EditorApplication.timeSinceStartup + 5.0;
        EditorApplication.update += PollUnitySkillsStart;
    }

    static void SetDirectoryEnv(string variableName, string path)
    {
        Directory.CreateDirectory(path);
        Environment.SetEnvironmentVariable(variableName, path, EnvironmentVariableTarget.Process);
    }

    static void TryStartUnitySkillsServer()
    {
        try
        {
            var serverType = FindType("UnitySkills.SkillsHttpServer");
            if (serverType == null)
            {
                RetryUnitySkillsStart();
                return;
            }

            var isRunning = serverType.GetProperty("IsRunning", BindingFlags.Public | BindingFlags.Static);
            if (isRunning != null && isRunning.GetValue(null) is bool running && running)
                return;

            var autoStart = serverType.GetProperty("AutoStart", BindingFlags.Public | BindingFlags.Static);
            autoStart?.SetValue(null, true);

            var preferredPort = serverType.GetProperty("PreferredPort", BindingFlags.Public | BindingFlags.Static);
            preferredPort?.SetValue(null, 8090);

            var start = serverType.GetMethod("Start", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int), typeof(bool) }, null);
            start?.Invoke(null, new object[] { 8090, true });
            EditorApplication.update -= PollUnitySkillsStart;
            UnityEngine.Debug.Log("[Codex] UnitySkills auto-start requested on port 8090.");
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[Codex] UnitySkills auto-start skipped: {ex.Message}");
            RetryUnitySkillsStart();
        }
    }

    static Type FindType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(fullName);
            if (type != null)
                return type;
        }

        return null;
    }

    static void RetryUnitySkillsStart()
    {
        if (++unitySkillsStartAttempts > 8)
        {
            EditorApplication.update -= PollUnitySkillsStart;
            UnityEngine.Debug.LogWarning("[Codex] UnitySkills auto-start skipped: UnitySkills.SkillsHttpServer was not found.");
            return;
        }

        nextUnitySkillsStartAttempt = EditorApplication.timeSinceStartup + 5.0;
    }

    static void PollUnitySkillsStart()
    {
        if (EditorApplication.timeSinceStartup < nextUnitySkillsStartAttempt)
            return;

        TryStartUnitySkillsServer();
    }
}
