namespace GagSpeak.Achievements;

public class TimedProgressAchievement : Achievement
{
    /// <summary>
    /// The Current Progress made towards the achievement.
    /// </summary>
    public int Progress { get; private set; }

    /// <summary>
    /// The Milestone that must be met to complete the achievement.
    /// </summary>
    public int MilestoneGoal { get; private set; }

    /// <summary>
    /// The DateTime when the progress went from 0 to 1
    /// </summary>
    private DateTime StartTime;

    /// <summary>
    /// How long you have to earn the achievement in.
    /// </summary>
    private TimeSpan TimeToComplete;


    public TimedProgressAchievement(string title, string description, int goal, TimeSpan timeLimit)
        : base(title, description)
    {
        MilestoneGoal = goal;
        TimeToComplete = timeLimit;
        Progress = 0;
    }

    /// <summary>
    /// Increments the progress towards the achievement.
    /// </summary>
    public void IncrementProgress(int amount = 1)
    {
        CheckTimeLimit();
        Progress += amount;
        // check for completion after incrementing progress
        CheckCompletion();
    }

    private void CheckTimeLimit()
    {
        // start the timer if the progress is 0
        if (Progress == 0)
        {
            StartTime = DateTime.UtcNow;
        }
        // reset the progress if we've exceeded the required time limit.
        else if (DateTime.UtcNow - StartTime >= TimeToComplete)
        {
            ResetProgress();
        }
    }

    /// <summary>
    /// Reset the progression of the achievement.
    /// </summary>
    public void ResetProgress() => Progress = 0;


    /// <summary>
    /// Check if the Milestone has been met.
    /// </summary>
    public override void CheckCompletion()
    {
        if (Progress >= MilestoneGoal)
        {
            // Mark the achievement as completed
            MarkCompleted();
        }
    }
}
