namespace Clube.Domain.Memberships;

public readonly record struct Credits
{
    public static readonly Credits Zero = new(0);

    public int Value { get; }

    private Credits(int value) => Value = value;

    public static Credits Of(int value) =>
        value >= 0 ? new Credits(value) : throw new ArgumentOutOfRangeException(nameof(value), value, "Credits cannot be negative.");

    public bool Covers(Credits cost) => Value >= cost.Value;

    public Credits Add(Credits other) => new(Value + other.Value);

    public Credits Subtract(Credits cost) => Of(Value - cost.Value);

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
