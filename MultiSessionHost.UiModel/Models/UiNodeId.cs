namespace MultiSessionHost.UiModel.Models;

public readonly record struct UiNodeId
{
    public UiNodeId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("UiNodeId cannot be empty.", nameof(value));
        }

        Value = value.Trim();
    }

    public string Value { get; }

    public static UiNodeId Parse(string value) => new(value);

    public static implicit operator string(UiNodeId nodeId) => nodeId.Value;

    public override string ToString() => Value;
}
