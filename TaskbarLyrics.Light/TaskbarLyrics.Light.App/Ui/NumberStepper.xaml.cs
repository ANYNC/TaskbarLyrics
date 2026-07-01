using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TaskbarLyrics.Light.App.Ui;

public partial class NumberStepper : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(NumberStepper),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(NumberStepper), new PropertyMetadata(0.0));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(NumberStepper), new PropertyMetadata(100.0));

    public static readonly DependencyProperty StepProperty =
        DependencyProperty.Register(nameof(Step), typeof(double), typeof(NumberStepper), new PropertyMetadata(1.0));

    public static readonly DependencyProperty DecimalsProperty =
        DependencyProperty.Register(nameof(Decimals), typeof(int), typeof(NumberStepper), new PropertyMetadata(0, OnValueChanged));

    public event EventHandler? ValueChanged;

    public NumberStepper()
    {
        InitializeComponent();
        DecreaseButton.Click += (_, _) => Adjust(-Step);
        IncreaseButton.Click += (_, _) => Adjust(Step);
        ValueBox.GotKeyboardFocus += (_, _) => ValueBox.SelectAll();
        ValueBox.LostKeyboardFocus += (_, _) => CommitText();
        ValueBox.PreviewKeyDown += OnValueBoxPreviewKeyDown;
        UpdateDisplay();
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public int Decimals
    {
        get => (int)GetValue(DecimalsProperty);
        set => SetValue(DecimalsProperty, value);
    }

    private void Adjust(double delta)
    {
        SetValueFromUser(Value + delta);
    }

    private void OnValueBoxPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                CommitText();
                ValueBox.SelectAll();
                e.Handled = true;
                break;
            case Key.Escape:
                UpdateDisplay();
                ValueBox.SelectAll();
                e.Handled = true;
                break;
            case Key.Up:
                Adjust(Step);
                ValueBox.SelectAll();
                e.Handled = true;
                break;
            case Key.Down:
                Adjust(-Step);
                ValueBox.SelectAll();
                e.Handled = true;
                break;
        }
    }

    private void CommitText()
    {
        var text = ValueBox.Text.Trim();
        if (TryParseValue(text, out var parsed))
        {
            SetValueFromUser(parsed);
            return;
        }

        UpdateDisplay();
    }

    private static bool TryParseValue(string text, out double value)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private void SetValueFromUser(double value)
    {
        var rounded = Decimals <= 0
            ? Math.Round(value)
            : Math.Round(value, Decimals);
        var clamped = Math.Clamp(rounded, Minimum, Maximum);
        var changed = Math.Abs(Value - clamped) > 0.0001;
        if (changed)
        {
            Value = clamped;
        }
        else
        {
            UpdateDisplay();
        }

        if (changed)
        {
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NumberStepper stepper)
        {
            stepper.UpdateDisplay();
        }
    }

    private void UpdateDisplay()
    {
        ValueBox.Text = Decimals <= 0
            ? Math.Round(Value).ToString(CultureInfo.InvariantCulture)
            : Math.Round(Value, Decimals).ToString($"F{Decimals}", CultureInfo.InvariantCulture);
    }
}
