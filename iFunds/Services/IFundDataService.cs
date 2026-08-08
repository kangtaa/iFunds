using System.Collections.Generic;
using System.Threading.Tasks;
using iFunds.Models;

namespace iFunds.Services;

public interface IFundDataService
{
    Task<Fund?> FetchFundAsync(string code);
    Task<IReadOnlyList<Fund>> RefreshAsync(IEnumerable<string> codes);
}
