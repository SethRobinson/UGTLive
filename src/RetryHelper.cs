using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace UGTLive
{
    public static class RetryHelper
    {
        public static async Task<T?> ExecuteWithRetryAsync<T>(
            Func<CancellationToken, Task<T?>> action,
            CancellationToken cancellationToken,
            int maxRetries = 3,
            int baseDelayMs = 10000,
            Action<int, Exception>? onRetry = null) where T : class
        {
            int attempt = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await action(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (!IsRetryableException(ex) || attempt >= maxRetries)
                        throw;

                    attempt++;
                    int delayMs = baseDelayMs * (1 << (attempt - 1));
                    onRetry?.Invoke(attempt, ex);
                    Console.WriteLine($"[RetryHelper] Attempt {attempt}/{maxRetries} failed ({ex.Message}), retrying in {delayMs / 1000}s...");
                    await Task.Delay(delayMs, cancellationToken);
                }
            }
        }

        public static async Task<T?> ExecuteWithRetryAsync<T>(
            Func<CancellationToken, Task<T?>> action,
            CancellationToken cancellationToken,
            int maxRetries,
            int baseDelayMs,
            Action<int, Exception>? onRetry) where T : struct
        {
            int attempt = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await action(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (!IsRetryableException(ex) || attempt >= maxRetries)
                        throw;

                    attempt++;
                    int delayMs = baseDelayMs * (1 << (attempt - 1));
                    onRetry?.Invoke(attempt, ex);
                    Console.WriteLine($"[RetryHelper] Attempt {attempt}/{maxRetries} failed ({ex.Message}), retrying in {delayMs / 1000}s...");
                    await Task.Delay(delayMs, cancellationToken);
                }
            }
        }

        /// <summary>
        /// Wraps an HttpResponseMessage-returning call with retry on retryable status codes.
        /// Returns the response on success or the last response on non-retryable / exhausted retries.
        /// </summary>
        public static async Task<HttpResponseMessage> SendWithRetryAsync(
            Func<CancellationToken, Task<HttpResponseMessage>> sendAction,
            CancellationToken cancellationToken,
            int maxRetries = 3,
            int baseDelayMs = 10000,
            Action<int, HttpStatusCode>? onRetry = null)
        {
            int attempt = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                HttpResponseMessage response;
                try
                {
                    response = await sendAction(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (HttpRequestException ex) when (attempt < maxRetries && IsRetryableException(ex))
                {
                    attempt++;
                    int delayMs = baseDelayMs * (1 << (attempt - 1));
                    onRetry?.Invoke(attempt, HttpStatusCode.ServiceUnavailable);
                    Console.WriteLine($"[RetryHelper] HTTP attempt {attempt}/{maxRetries} failed ({ex.Message}), retrying in {delayMs / 1000}s...");
                    await Task.Delay(delayMs, cancellationToken);
                    continue;
                }

                if (response.IsSuccessStatusCode || !IsRetryableStatusCode(response.StatusCode) || attempt >= maxRetries)
                    return response;

                attempt++;
                int delay = baseDelayMs * (1 << (attempt - 1));
                onRetry?.Invoke(attempt, response.StatusCode);
                Console.WriteLine($"[RetryHelper] HTTP {(int)response.StatusCode} on attempt {attempt}/{maxRetries}, retrying in {delay / 1000}s...");
                response.Dispose();
                await Task.Delay(delay, cancellationToken);
            }
        }

        public static bool IsRetryableStatusCode(HttpStatusCode code)
        {
            return code == HttpStatusCode.TooManyRequests ||
                   code == HttpStatusCode.InternalServerError ||
                   code == HttpStatusCode.BadGateway ||
                   code == HttpStatusCode.ServiceUnavailable ||
                   code == HttpStatusCode.GatewayTimeout;
        }

        public static bool IsRetryableException(Exception ex)
        {
            if (ex is HttpRequestException httpEx)
            {
                if (httpEx.StatusCode.HasValue && !IsRetryableStatusCode(httpEx.StatusCode.Value))
                    return false;

                string msg = ex.Message;
                if (msg.Contains("401") || msg.Contains("403") ||
                    msg.Contains("Unauthorized") || msg.Contains("Forbidden"))
                    return false;

                return true;
            }

            if (ex is TaskCanceledException)
                return false;

            if (ex is System.IO.IOException)
                return true;

            return false;
        }
    }
}
