namespace API.RequestHelpers
{
    public class PaginationParams
    {
        private const int MaxPageSize = 50;

        // These come straight off the query string, so both need a lower bound
        // as well as an upper one: pageNumber=0 makes Skip() negative, and
        // pageSize=0 divides by zero when TotalPages is calculated.
        private int _pageNumber = 1;
        public int PageNumber
        {
            get => _pageNumber;
            set => _pageNumber = value < 1 ? 1 : value;
        }

        private int _pageSize = 6;
        public int PageSize
        {
            get => _pageSize;
            set => _pageSize = value < 1 ? 1 : (value > MaxPageSize ? MaxPageSize : value);
        }
    }
}