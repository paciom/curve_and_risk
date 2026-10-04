namespace Shop.Common;

public sealed class RetryPolicy(int maxAttempts, TimeSpan baseDelay, TimeProvider clock)
{
    private readonly int _maxAttempts = maxAttempts >= 1
        ? maxAttempts
        : throw new ArgumentOutOfRangeException(nameof(maxAttempts), "At least one attempt is required.");

    /// <summary>Runs the operation, retrying transient failures with exponential backoff. The last failure is rethrown.</summary>
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (TransientException) when (attempt < _maxAttempts)
            {
                var delay = baseDelay * Math.Pow(2, attempt - 1);
                await Task.Delay(delay, clock, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
