using System.Collections.Concurrent;
using MindMatchAI.Models.Runtime;
namespace MindMatchAI.Services.InterviewFlow;
public class InterviewRuntimeStore
{
    //Holds active interviews in memory instead of the database

    private readonly ConcurrentDictionary<Guid, InterviewRuntimeState> _activeInterviews = new();
    public InterviewRuntimeState CreateOrReplace(InterviewRuntimeState state)
    {
        _activeInterviews[state.InterviewId] = state;
        return state;
    }
    public InterviewRuntimeState GetOrThrow(Guid interviewId)
    {
        if (_activeInterviews.TryGetValue(interviewId, out var state))
        {
            return state;
        }
        throw new Exception($"Runtime interview state not found for interviewId: {interviewId}");
    }
    public bool TryGet(Guid interviewId, out InterviewRuntimeState? state)
    {
        return _activeInterviews.TryGetValue(interviewId, out state);
    }
    public void Update(InterviewRuntimeState state)
    {
        state.LastUpdatedAtUtc = DateTime.UtcNow;
        _activeInterviews[state.InterviewId] = state;
    }
    public bool Remove(Guid interviewId)
    {
        return _activeInterviews.TryRemove(interviewId, out _);
    }
    public int CountActiveInterviews()
    {
        return _activeInterviews.Count;
    }
}