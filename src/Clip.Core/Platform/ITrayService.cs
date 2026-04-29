namespace Clip.Core.Platform;

public interface ITrayService : IDisposable
{
    void Show();
    void Hide();
    void SetBusy(bool isBusy);
}
