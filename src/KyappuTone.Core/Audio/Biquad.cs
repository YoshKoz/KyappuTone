namespace KyappuTone.Core.Audio;

/// <summary>
/// Single second-order peaking EQ section (RBJ cookbook), normalised so the
/// transfer function is <c>H(z) = (b0 + b1 z^-1 + b2 z^-2) / (1 + a1 z^-1 + a2 z^-2)</c>.
/// </summary>
public sealed class Biquad
{
    private readonly float _sampleRate;
    private readonly float _b0;
    private readonly float _b1;
    private readonly float _b2;
    private readonly float _a1;
    private readonly float _a2;

    private float _x1;
    private float _x2;
    private float _y1;
    private float _y2;

    /// <summary>Creates a peaking section at <paramref name="frequency"/> with the given Q and gain.</summary>
    public Biquad(float sampleRate, float frequency, float q, float gainDb)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1f);
        ArgumentOutOfRangeException.ThrowIfLessThan(frequency, 1f);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(frequency, sampleRate / 2f);
        ArgumentOutOfRangeException.ThrowIfLessThan(q, 0.01f);

        _sampleRate = sampleRate;

        if (gainDb == 0f)
        {
            // Exact bypass: avoid coefficient rounding so a flat EQ is bit-transparent.
            _b0 = 1f;
            _b1 = _b2 = _a1 = _a2 = 0f;
            return;
        }

        var a = MathF.Pow(10f, gainDb / 40f);
        var omega = 2f * MathF.PI * frequency / sampleRate;
        var sin = MathF.Sin(omega);
        var cos = MathF.Cos(omega);
        var alpha = sin / (2f * q);

        var b0 = 1f + alpha * a;
        var b1 = -2f * cos;
        var b2 = 1f - alpha * a;
        var a0 = 1f + alpha / a;
        var a1 = -2f * cos;
        var a2 = 1f - alpha / a;

        _b0 = b0 / a0;
        _b1 = b1 / a0;
        _b2 = b2 / a0;
        _a1 = a1 / a0;
        _a2 = a2 / a0;
    }

    /// <summary>Processes one sample through the section.</summary>
    public float Process(float x)
    {
        var y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;

        _x2 = _x1;
        _x1 = x;
        _y2 = _y1;
        _y1 = y;

        return y;
    }

    /// <summary>Clears the filter state so the section behaves like a fresh instance.</summary>
    public void Reset()
    {
        _x1 = _x2 = _y1 = _y2 = 0f;
    }

    /// <summary>Evaluates the magnitude and phase response at <paramref name="frequency"/>.</summary>
    public (float Magnitude, float Phase) ResponseAt(float frequency)
    {
        var omega = 2f * MathF.PI * frequency / _sampleRate;
        var z1 = new Complex(MathF.Cos(-omega), MathF.Sin(-omega));
        var z2 = z1 * z1;

        var numerator = new Complex(_b0, 0) + _b1 * z1 + _b2 * z2;
        var denominator = Complex.One + _a1 * z1 + _a2 * z2;
        var response = numerator / denominator;

        return ((float)response.Magnitude, (float)response.Phase);
    }

    private readonly record struct Complex(double Real, double Imaginary)
    {
        public static Complex One => new(1, 0);
        public static Complex operator +(Complex left, Complex right) => new(left.Real + right.Real, left.Imaginary + right.Imaginary);
        public static Complex operator -(Complex left, Complex right) => new(left.Real - right.Real, left.Imaginary - right.Imaginary);
        public static Complex operator *(Complex left, Complex right) => new(
            left.Real * right.Real - left.Imaginary * right.Imaginary,
            left.Real * right.Imaginary + left.Imaginary * right.Real);
        public static Complex operator *(Complex left, double scalar) => new(left.Real * scalar, left.Imaginary * scalar);
        public static Complex operator *(float scalar, Complex value) => new(value.Real * scalar, value.Imaginary * scalar);
        public static Complex operator /(Complex left, Complex right)
        {
            var denom = right.Real * right.Real + right.Imaginary * right.Imaginary;
            return new(
                (left.Real * right.Real + left.Imaginary * right.Imaginary) / denom,
                (left.Imaginary * right.Real - left.Real * right.Imaginary) / denom);
        }

        public double Magnitude => Math.Sqrt(Real * Real + Imaginary * Imaginary);
        public double Phase => Math.Atan2(Imaginary, Real);
    }
}
