using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public sealed class XRTrainingAIVector3
{
    public float x;
    public float y;
    public float z;

    public static XRTrainingAIVector3 From(Vector3 value)
    {
        return new XRTrainingAIVector3 { x = value.x, y = value.y, z = value.z };
    }
}

[Serializable]
public sealed class XRTrainingAIObjectSnapshot
{
    public string name;
    public string color;
    public bool scored;
    public XRTrainingAIVector3 position;
    public string currentZone;
}

[Serializable]
public sealed class XRTrainingAIStateSnapshot
{
    public string userId;
    public string taskId;
    public int trialNumber;
    public string condition;
    public string difficulty;
    public string state;
    public float elapsedSeconds;
    public float timeLimitSeconds;
    public int score;
    public int requiredScore;
    public int correctCount;
    public int wrongCount;
    public int grabCount;
    public int releaseCount;
    public int teleportCount;
    public int resetCount;
    public bool success;
    public string lastEventType;
    public string lastObjectName;
    public string lastEventDetails;
    public string[] recentEvents;
    public string[] remainingGoals;
    public XRTrainingAIObjectSnapshot[] objects;
    public XRTrainingAIObjectSnapshot[] remainingObjects;
}

[Serializable]
public sealed class XRTrainingAIResponse
{
    public string hintText;
    public string suggestedAction;
    public string targetObject;
    public string reason;
    public string summaryText;
    public string nextRoundSuggestion;
}

[Serializable]
sealed class XRTrainingChatMessage
{
    public string role;
    public string content;
}

[Serializable]
sealed class XRTrainingChatRequest
{
    public string model;
    public XRTrainingChatMessage[] messages;
    public float temperature = 0.2f;
}

[Serializable]
sealed class XRTrainingChatChoice
{
    public XRTrainingChatMessage message;
}

[Serializable]
sealed class XRTrainingChatResponse
{
    public XRTrainingChatChoice[] choices;
}

public sealed class XRTrainingAIAssistant : MonoBehaviour
{
    [Header("API")]
    public bool enableNetworkRequests = true;
    public string endpointUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions";
    public string model = "qwen-plus";
    public string apiKeyOverride;
    public string apiKeyEnvironmentVariable = "DASHSCOPE_API_KEY";
    public float requestTimeoutSeconds = 8f;

    public bool IsBusy { get; private set; }

    public IEnumerator RequestHint(XRTrainingAIStateSnapshot snapshot, string trigger, Action<XRTrainingAIResponse, string, bool> onComplete)
    {
        yield return RequestModel(snapshot, trigger, false, onComplete);
    }

    public IEnumerator RequestSummary(XRTrainingAIStateSnapshot snapshot, string trigger, Action<XRTrainingAIResponse, string, bool> onComplete)
    {
        yield return RequestModel(snapshot, trigger, true, onComplete);
    }

    IEnumerator RequestModel(XRTrainingAIStateSnapshot snapshot, string trigger, bool summary, Action<XRTrainingAIResponse, string, bool> onComplete)
    {
        if (IsBusy)
        {
            onComplete?.Invoke(Fallback(snapshot, trigger, summary), "busy", false);
            yield break;
        }

        IsBusy = true;
        string stateJson = JsonUtility.ToJson(snapshot, true);
        string apiKey = ResolveApiKey();
        if (!enableNetworkRequests || string.IsNullOrWhiteSpace(endpointUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            yield return null;
            IsBusy = false;
            onComplete?.Invoke(Fallback(snapshot, trigger, summary), "fallback: api not configured", false);
            yield break;
        }

        string prompt = BuildPrompt(stateJson, trigger, summary);
        string requestJson = JsonUtility.ToJson(new XRTrainingChatRequest
        {
            model = string.IsNullOrWhiteSpace(model) ? "qwen-plus" : model,
            messages = new[]
            {
                new XRTrainingChatMessage { role = "system", content = SystemPrompt(summary) },
                new XRTrainingChatMessage { role = "user", content = prompt }
            }
        });

        using (var request = new UnityWebRequest(endpointUrl, UnityWebRequest.kHttpVerbPOST))
        {
            byte[] body = Encoding.UTF8.GetBytes(requestJson);
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = Mathf.Max(1, Mathf.RoundToInt(requestTimeoutSeconds));
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            yield return request.SendWebRequest();

            string raw = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
            bool ok = request.result == UnityWebRequest.Result.Success;
            XRTrainingAIResponse parsed = ok ? ParseResponse(raw) : null;
            if (parsed == null || string.IsNullOrWhiteSpace(summary ? parsed.summaryText : parsed.hintText))
            {
                IsBusy = false;
                onComplete?.Invoke(Fallback(snapshot, trigger, summary), string.IsNullOrEmpty(raw) ? request.error : raw, false);
                yield break;
            }

            IsBusy = false;
            onComplete?.Invoke(parsed, raw, true);
        }
    }

    static string SystemPrompt(bool summary)
    {
        if (summary)
            return "You are an XR training experiment assistant. Return strict JSON only with summaryText and nextRoundSuggestion. Keep it concise and based only on the provided state.";

        return "You are an XR training assistant. Return strict JSON only with hintText, suggestedAction, targetObject, and reason. Give one short actionable hint based only on the provided state.";
    }

    static string BuildPrompt(string stateJson, string trigger, bool summary)
    {
        string schema = summary
            ? "{\"summaryText\":\"...\",\"nextRoundSuggestion\":\"...\"}"
            : "{\"hintText\":\"...\",\"suggestedAction\":\"...\",\"targetObject\":\"...\",\"reason\":\"...\"}";
        return "Trigger: " + trigger + "\nState JSON:\n" + stateJson + "\nReturn JSON exactly in this shape:\n" + schema;
    }

    string ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(apiKeyOverride))
            return apiKeyOverride.Trim();

        if (!string.IsNullOrWhiteSpace(apiKeyEnvironmentVariable))
            return Environment.GetEnvironmentVariable(apiKeyEnvironmentVariable);

        return string.Empty;
    }

    static XRTrainingAIResponse ParseResponse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        string content = raw;
        try
        {
            var chat = JsonUtility.FromJson<XRTrainingChatResponse>(raw);
            if (chat != null && chat.choices != null && chat.choices.Length > 0 && chat.choices[0].message != null && !string.IsNullOrWhiteSpace(chat.choices[0].message.content))
                content = chat.choices[0].message.content;
        }
        catch (ArgumentException)
        {
        }

        content = StripCodeFence(content);
        try
        {
            return JsonUtility.FromJson<XRTrainingAIResponse>(content);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    static string StripCodeFence(string value)
    {
        value = value.Trim();
        if (!value.StartsWith("```", StringComparison.Ordinal))
            return value;

        int firstLine = value.IndexOf('\n');
        int lastFence = value.LastIndexOf("```", StringComparison.Ordinal);
        if (firstLine >= 0 && lastFence > firstLine)
            return value.Substring(firstLine + 1, lastFence - firstLine - 1).Trim();

        return value.Trim('`').Trim();
    }

    static XRTrainingAIResponse Fallback(XRTrainingAIStateSnapshot snapshot, string trigger, bool summary)
    {
        if (summary)
        {
            return new XRTrainingAIResponse
            {
                summaryText = "Result: " + (snapshot.success ? "success" : "not complete") + ", score " + snapshot.score + "/" + snapshot.requiredScore + ", time " + snapshot.elapsedSeconds.ToString("0.0") + "s.",
                nextRoundSuggestion = snapshot.wrongCount > 0 ? "Match each cube color to the same color target before releasing." : "Keep the same order and move cubes one at a time."
            };
        }

        string target = FirstRemainingObject(snapshot);
        string hint = string.IsNullOrEmpty(target)
            ? "All cubes are matched. Move to the Finish zone to show results."
            : "Grab " + target + " and place it on the matching color target.";

        if (!string.IsNullOrEmpty(trigger) && trigger.IndexOf("WrongPlacement", StringComparison.OrdinalIgnoreCase) >= 0)
            hint = "That cube is on the wrong target. Match the cube color with the same color target.";

        return new XRTrainingAIResponse
        {
            hintText = hint,
            suggestedAction = string.IsNullOrEmpty(target) ? "GoFinish" : "GrabAndPlace",
            targetObject = target,
            reason = "Generated from current task state fallback."
        };
    }

    static string FirstRemainingObject(XRTrainingAIStateSnapshot snapshot)
    {
        if (snapshot == null || snapshot.remainingObjects == null)
            return string.Empty;

        for (int i = 0; i < snapshot.remainingObjects.Length; i++)
        {
            if (snapshot.remainingObjects[i] != null && !string.IsNullOrEmpty(snapshot.remainingObjects[i].name))
                return snapshot.remainingObjects[i].name;
        }

        return string.Empty;
    }
}
