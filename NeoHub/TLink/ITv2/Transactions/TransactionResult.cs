using DSC.TLink.ITv2.Messages;

namespace DSC.TLink.ITv2.Transactions
{
    public record TransactionResult
    {
        public IMessageData CommandMessage { get; set; }
        public IMessageData ResponseMessage { get; init; }
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }
        public TransactionResult() 
        { 
                Success = true;
                ResponseMessage = null!;
                ErrorMessage = null;
        }
        public TransactionResult(IMessageData MessageData)
        {
            Success = true;
            this.ResponseMessage = MessageData;
            ErrorMessage = null;
        }
        public TransactionResult(string ErrorMessage)
        {
            Success = false;
            ResponseMessage = null!;
            this.ErrorMessage = ErrorMessage;
        }
    }
}
