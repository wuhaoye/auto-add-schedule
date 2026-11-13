namespace AutoAddSchedule.Services;

public interface IReminderService : IDisposable
{
    void Start();

    void Stop();
}
