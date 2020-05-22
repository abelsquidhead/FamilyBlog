using System.Threading.Tasks;
using OrchardCore.ContentManagement;
using OrchardCore.DisplayManagement.ModelBinding;
using YesSql;

namespace OrchardCore.Contents.Services
{
    public interface IContentAdminListQueryProvider
    {
        Task<IQuery<ContentItem>> ProvideQueryAsync(IUpdateModel updateModel);
    }
}
