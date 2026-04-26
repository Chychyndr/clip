namespace Clip.Models;

public sealed class ClipRange : ObservableEntity
{
    private bool _isEnabled;
    private double _durationSeconds;
    private double _startSeconds;
    private double _endSeconds;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public double DurationSeconds
    {
        get => _durationSeconds;
        set
        {
            var next = Math.Max(0, value);
            if (!SetProperty(ref _durationSeconds, next))
            {
                return;
            }

            if (_endSeconds <= 0 || _endSeconds > next)
            {
                EndSeconds = next;
            }

            StartSeconds = Math.Min(_startSeconds, Math.Max(0, EndSeconds - 1));
            OnPropertyChanged(nameof(StartLabel));
            OnPropertyChanged(nameof(EndLabel));
        }
    }

    public double StartSeconds
    {
        get => _startSeconds;
        set
        {
            var maxStart = Math.Max(0, EndSeconds - 1);
            var next = Math.Clamp(value, 0, maxStart);
            if (SetProperty(ref _startSeconds, next))
            {
                OnPropertyChanged(nameof(StartLabel));
            }
        }
    }

    public double EndSeconds
    {
        get => _endSeconds;
        set
        {
            var minEnd = Math.Min(DurationSeconds, StartSeconds + 1);
            var next = DurationSeconds <= 0
                ? 0
                : Math.Clamp(value, minEnd, DurationSeconds);
            if (SetProperty(ref _endSeconds, next))
            {
                OnPropertyChanged(nameof(EndLabel));
            }
        }
    }

    public string StartLabel => FormatTime(StartSeconds);
    public string EndLabel => FormatTime(EndSeconds);
    public double LengthSeconds => Math.Max(0, EndSeconds - StartSeconds);

    public static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss")
            : time.ToString(@"m\:ss");
    }
}
