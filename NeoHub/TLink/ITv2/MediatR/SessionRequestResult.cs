namespace DSC.TLink.ITv2.MediatR
{
    internal record SessionRequestResult
    {
        public required Func<CancellationToken, Task<Result>> Continuation { get; init; }
    }
}
