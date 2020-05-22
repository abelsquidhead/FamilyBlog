using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Display;
using OrchardCore.ContentManagement.Metadata;
using OrchardCore.ContentManagement.Metadata.Models;
using OrchardCore.ContentManagement.Metadata.Settings;
using OrchardCore.ContentManagement.Records;
using OrchardCore.Contents.Services;
using OrchardCore.Contents.ViewModels;
using OrchardCore.DisplayManagement;
using OrchardCore.DisplayManagement.ModelBinding;
using OrchardCore.DisplayManagement.Notify;
using OrchardCore.DisplayManagement.Shapes;
using OrchardCore.DisplayManagement.Zones;
using OrchardCore.Modules;
using OrchardCore.Navigation;
using OrchardCore.Routing;
using OrchardCore.Settings;
using YesSql;
using YesSql.Services;

namespace OrchardCore.Contents.Controllers
{
    public class AdminController : Controller
    {
        private readonly IContentManager _contentManager;
        private readonly IContentDefinitionManager _contentDefinitionManager;
        private readonly ISiteService _siteService;
        private readonly ISession _session;
        private readonly IContentItemDisplayManager _contentItemDisplayManager;
        private readonly INotifier _notifier;
        private readonly IAuthorizationService _authorizationService;
        private readonly IContentAdminListQueryProvider _contentAdminListQueryProvider;
        private readonly IEnumerable<IContentAdminRouteValueProvider> _contentAdminRouteValueProviders;
        private readonly IHtmlLocalizer H;
        private readonly IStringLocalizer S;
        private readonly IUpdateModelAccessor _updateModelAccessor;
        private readonly IShapeFactory _shapeFactory;
        private readonly dynamic New;
        private readonly ILogger _logger;

        public AdminController(
            IAuthorizationService authorizationService,
            IContentManager contentManager,
            IContentItemDisplayManager contentItemDisplayManager,
            IContentDefinitionManager contentDefinitionManager,
            ISiteService siteService,
            INotifier notifier,
            ISession session,
            IShapeFactory shapeFactory,
            IContentAdminListQueryProvider contentAdminListQueryProvider,
            IEnumerable<IContentAdminRouteValueProvider> contentAdminRouteValueProviders,
            ILogger<AdminController> logger,
            IHtmlLocalizer<AdminController> htmlLocalizer,
            IStringLocalizer<AdminController> stringLocalizer,
            IUpdateModelAccessor updateModelAccessor)
        {
            _authorizationService = authorizationService;
            _notifier = notifier;
            _contentItemDisplayManager = contentItemDisplayManager;
            _session = session;
            _siteService = siteService;
            _contentManager = contentManager;
            _contentDefinitionManager = contentDefinitionManager;
            _updateModelAccessor = updateModelAccessor;
            _contentAdminListQueryProvider = contentAdminListQueryProvider;
            _contentAdminRouteValueProviders = contentAdminRouteValueProviders;

            H = htmlLocalizer;
            S = stringLocalizer;
            _shapeFactory = shapeFactory;
            New = shapeFactory;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> List(ListContentsViewModel model, PagerParameters pagerParameters, string contentTypeId = "")
        {
            var siteSettings = await _siteService.GetSiteSettingsAsync();
            var pager = new Pager(pagerParameters, siteSettings.PageSize);

            // This is used by the AdminMenus so needs to be passed into the options.
            if (!string.IsNullOrEmpty(contentTypeId))
            {
                model.Options.SelectedContentType = contentTypeId;
            }

            // Populate the creatable types.
            if (!string.IsNullOrEmpty(model.Options.SelectedContentType))
            {
                var contentTypeDefinition = _contentDefinitionManager.GetTypeDefinition(model.Options.SelectedContentType);
                if (contentTypeDefinition != null)
                {
                    // Allows non creatable types to be created by another admin page.
                    if (model.Options.CanCreateSelectedContentType)
                    {
                        model.Options.CreatableTypes = new List<SelectListItem>
                        {
                            new SelectListItem(new LocalizedString(contentTypeDefinition.DisplayName, contentTypeDefinition.DisplayName).Value, contentTypeDefinition.Name)
                        };
                    }
                }
            }

            if (model.Options.CreatableTypes == null)
            {
                var contentTypes = _contentDefinitionManager
                    .ListTypeDefinitions()
                    .Where(ctd => ctd.GetSettings<ContentTypeSettings>().Creatable)
                    .OrderBy(ctd => ctd.DisplayName);

                var creatableList = new List<SelectListItem>();
                if (contentTypes.Any())
                {
                    foreach (var contentTypeDefinition in contentTypes)
                    {
                        creatableList.Add(new SelectListItem(new LocalizedString(contentTypeDefinition.DisplayName, contentTypeDefinition.DisplayName).Value, contentTypeDefinition.Name));
                    }
                }

                model.Options.CreatableTypes = creatableList;
            }

            //We populate the remaining SelectLists
            model.Options.ContentStatuses = new List<SelectListItem>()
            {
                new SelectListItem() { Text = S["Latest"], Value = nameof(ContentsStatus.Latest) },
                new SelectListItem() { Text = S["Owned by me"], Value = nameof(ContentsStatus.Owner) },
                new SelectListItem() { Text = S["Published"], Value = nameof(ContentsStatus.Published) },
                new SelectListItem() { Text = S["Unpublished"], Value = nameof(ContentsStatus.Draft) },
                new SelectListItem() { Text = S["All versions"], Value = nameof(ContentsStatus.AllVersions) }
            };

            model.Options.ContentSorts = new List<SelectListItem>()
            {
                new SelectListItem() { Text = S["Recently created"], Value = nameof(ContentsOrder.Created) },
                new SelectListItem() { Text = S["Recently modified"], Value = nameof(ContentsOrder.Modified) },
                new SelectListItem() { Text = S["Recently published"], Value = nameof(ContentsOrder.Published) },
                new SelectListItem() { Text = S["Title"], Value = nameof(ContentsOrder.Title) }
            };

            model.Options.ContentsBulkAction = new List<SelectListItem>()
            {
                new SelectListItem() { Text = S["Publish Now"], Value = nameof(ContentsBulkAction.PublishNow) },
                new SelectListItem() { Text = S["Unpublish"], Value = nameof(ContentsBulkAction.Unpublish) },
                new SelectListItem() { Text = S["Delete"], Value = nameof(ContentsBulkAction.Remove) }
            };

            var listableTypes = new List<ContentTypeDefinition>();
            foreach (var ctd in _contentDefinitionManager.ListTypeDefinitions())
            {
                if (ctd.GetSettings<ContentTypeSettings>().Listable)
                {
                    var authorized = await _authorizationService.AuthorizeAsync(User, Permissions.EditContent, await _contentManager.NewAsync(ctd.Name));
                    if (authorized)
                    {
                        listableTypes.Add(ctd);
                    }
                }
            }

            var contentTypeOptions = listableTypes
                .Select(ctd => new KeyValuePair<string, string>(ctd.Name, ctd.DisplayName))
                .ToList().OrderBy(kvp => kvp.Value);

            model.Options.ContentTypeOptions = new List<SelectListItem>
            {
                new SelectListItem() { Text = S["All content types"], Value = "" }
            };
            foreach (var option in contentTypeOptions)
            {
                model.Options.ContentTypeOptions.Add(new SelectListItem() { Text = option.Value, Value = option.Key });
            }

            // With the model populated we filter the query, allowing the filters to mutate the view model.
            var query = await _contentAdminListQueryProvider.ProvideQueryAsync(_updateModelAccessor.ModelUpdater);

            var maxPagedCount = siteSettings.MaxPagedCount;
            if (maxPagedCount > 0 && pager.PageSize > maxPagedCount)
                pager.PageSize = maxPagedCount;

            //We prepare the pager
            var routeData = new RouteData();
            routeData.Values.Add("DisplayText", model.Options.DisplayText);

            var pagerShape = (await New.Pager(pager)).TotalItemCount(maxPagedCount > 0 ? maxPagedCount : await query.CountAsync()).RouteData(routeData);
            var pageOfContentItems = await query.Skip(pager.GetStartIndex()).Take(pager.PageSize).ListAsync();

            //We prepare the content items SummaryAdmin shape
            var contentItemSummaries = new List<dynamic>();
            foreach (var contentItem in pageOfContentItems)
            {
                contentItemSummaries.Add(await _contentItemDisplayManager.BuildDisplayAsync(contentItem, _updateModelAccessor.ModelUpdater, "SummaryAdmin"));
            }

            // The shape to listen for events here is ContentsAdminListHeader.
            var header = await CreateZoneShapeAsync("ContentsAdminListHeader");

            var search = await _shapeFactory.CreateAsync("ContentsAdminList__Search", BuildContentOptionsViewModel(model.Options));
            search.Metadata.Prefix = "Options";
            await AddToZone("Search", header, search, ":10");

            var create = await _shapeFactory.CreateAsync("ContentsAdminList__Create", BuildContentOptionsViewModel(model.Options));
            create.Metadata.Prefix = "Options";
            await AddToZone("Create", header, create, ":10");

            var startIndex = (pagerShape.Page - 1) * (pagerShape.PageSize) + 1;
            var summary = await _shapeFactory.CreateAsync("ContentsAdminList__Summary", Arguments.From(new
            {
                StartIndex = startIndex,
                EndIndex = startIndex + contentItemSummaries.Count - 1,
                ContentItemsCount = contentItemSummaries.Count,
                TotalItemCount = pagerShape.TotalItemCount
            }));
            await AddToZone("Summary", header, summary, ":10");

            var bulkActions = await CreateZoneShapeAsync("ContentsAdminListBulkActions");

            var bulksActionsShape = await _shapeFactory.CreateAsync("ContentsAdminList__BulkActions", BuildContentOptionsViewModel(model.Options));
            bulksActionsShape.Metadata.Prefix = "Options";

            await AddToZone("BulkActions", bulkActions, bulksActionsShape, ":10");

            await AddToZone("Actions", header, bulkActions, ":10");

            var filters = await _shapeFactory.CreateAsync("ContentsAdminList__Filters", BuildContentOptionsViewModel(model.Options));
            filters.Metadata.Prefix = "Options";
            await AddToZone("Actions", header, filters, ":10");

            var shapeViewModel = await _shapeFactory.CreateAsync<ListContentsViewModel>("ContentsAdminList", viewModel =>
            {
                viewModel.ContentItems = contentItemSummaries;
                viewModel.Pager = pagerShape;
                viewModel.Options = model.Options;
                viewModel.Header = header;
            });

            return View(shapeViewModel);
        }

        [HttpPost, ActionName("List")]
        [FormValueRequired("submit.Filter")]
        public async Task<ActionResult> ListFilterPOST(ListContentsViewModel model)
        {
            var routeValueDictionary = new RouteValueDictionary();

            await _contentAdminRouteValueProviders.InvokeAsync((routeValueProvider, updateModel, routeValueDictionary) => routeValueProvider.ProvideRouteValuesAsync(updateModel, routeValueDictionary), _updateModelAccessor.ModelUpdater, routeValueDictionary, _logger);

            return RedirectToAction("List", routeValueDictionary);
        }

        [HttpPost, ActionName("List")]
        [FormValueRequired("submit.BulkAction")]
        public async Task<ActionResult> ListPOST(ContentOptionsViewModel options, IEnumerable<int> itemIds)
        {
            if (itemIds?.Count() > 0)
            {
                var checkedContentItems = await _session.Query<ContentItem, ContentItemIndex>().Where(x => x.DocumentId.IsIn(itemIds) && x.Latest).ListAsync();
                switch (options.BulkAction)
                {
                    case ContentsBulkAction.None:
                        break;
                    case ContentsBulkAction.PublishNow:
                        foreach (var item in checkedContentItems)
                        {
                            if (!await _authorizationService.AuthorizeAsync(User, Permissions.PublishContent, item))
                            {
                                _notifier.Warning(H["Couldn't publish selected content."]);
                                _session.Cancel();
                                return Forbid();
                            }

                            await _contentManager.PublishAsync(item);
                        }
                        _notifier.Success(H["Content successfully published."]);
                        break;
                    case ContentsBulkAction.Unpublish:
                        foreach (var item in checkedContentItems)
                        {
                            if (!await _authorizationService.AuthorizeAsync(User, Permissions.PublishContent, item))
                            {
                                _notifier.Warning(H["Couldn't unpublish selected content."]);
                                _session.Cancel();
                                return Forbid();
                            }

                            await _contentManager.UnpublishAsync(item);
                        }
                        _notifier.Success(H["Content successfully unpublished."]);
                        break;
                    case ContentsBulkAction.Remove:
                        foreach (var item in checkedContentItems)
                        {
                            if (!await _authorizationService.AuthorizeAsync(User, Permissions.DeleteContent, item))
                            {
                                _notifier.Warning(H["Couldn't remove selected content."]);
                                _session.Cancel();
                                return Forbid();
                            }

                            await _contentManager.RemoveAsync(item);
                        }
                        _notifier.Success(H["Content successfully removed."]);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            return RedirectToAction("List");
        }

        public async Task<IActionResult> Create(string id)
        {
            if (String.IsNullOrWhiteSpace(id))
            {
                return NotFound();
            }

            var contentItem = await _contentManager.NewAsync(id);

            // Set the current user as the owner to check for ownership permissions on creation
            contentItem.Owner = User.Identity.Name;

            if (!await _authorizationService.AuthorizeAsync(User, Permissions.EditContent, contentItem))
            {
                return Forbid();
            }

            var model = await _contentItemDisplayManager.BuildEditorAsync(contentItem, _updateModelAccessor.ModelUpdater, true);

            return View(model);
        }

        [HttpPost, ActionName("Create")]
        [FormValueRequired("submit.Save")]
        public Task<IActionResult> CreatePOST(string id, [Bind(Prefix = "submit.Save")] string submitSave, string returnUrl)
        {
            var stayOnSamePage = submitSave == "submit.SaveAndContinue";
            return CreatePOST(id, returnUrl, stayOnSamePage, contentItem =>
            {
                var typeDefinition = _contentDefinitionManager.GetTypeDefinition(contentItem.ContentType);

                _notifier.Success(string.IsNullOrWhiteSpace(typeDefinition.DisplayName)
                    ? H["Your content draft has been saved."]
                    : H["Your {0} draft has been saved.", typeDefinition.DisplayName]);

                return Task.CompletedTask;
            });
        }

        [HttpPost, ActionName("Create")]
        [FormValueRequired("submit.Publish")]
        public async Task<IActionResult> CreateAndPublishPOST(string id, [Bind(Prefix = "submit.Publish")] string submitPublish, string returnUrl)
        {
            var stayOnSamePage = submitPublish == "submit.PublishAndContinue";
            // pass a dummy content to the authorization check to check for "own" variations
            var dummyContent = await _contentManager.NewAsync(id);

            // Set the current user as the owner to check for ownership permissions on creation
            dummyContent.Owner = User.Identity.Name;

            if (!await _authorizationService.AuthorizeAsync(User, Permissions.PublishContent, dummyContent))
            {
                return Forbid();
            }

            return await CreatePOST(id, returnUrl, stayOnSamePage, async contentItem =>
            {
                await _contentManager.PublishAsync(contentItem);

                var typeDefinition = _contentDefinitionManager.GetTypeDefinition(contentItem.ContentType);

                _notifier.Success(string.IsNullOrWhiteSpace(typeDefinition.DisplayName)
                    ? H["Your content has been published."]
                    : H["Your {0} has been published.", typeDefinition.DisplayName]);
            });
        }

        private async Task<IActionResult> CreatePOST(string id, string returnUrl, bool stayOnSamePage, Func<ContentItem, Task> conditionallyPublish)
        {
            var contentItem = await _contentManager.NewAsync(id);

            // Set the current user as the owner to check for ownership permissions on creation
            contentItem.Owner = User.Identity.Name;

            if (!await _authorizationService.AuthorizeAsync(User, Permissions.EditContent, contentItem))
            {
                return Forbid();
            }

            var model = await _contentItemDisplayManager.UpdateEditorAsync(contentItem, _updateModelAccessor.ModelUpdater, true);

            if (!ModelState.IsValid)
            {
                _session.Cancel();
                return View(model);
            }

            await _contentManager.CreateAsync(contentItem, VersionOptions.Draft);

            await conditionallyPublish(contentItem);

            if ((!string.IsNullOrEmpty(returnUrl)) && (!stayOnSamePage))
            {
                return LocalRedirect(returnUrl);
            }

            var adminRouteValues = (await _contentManager.PopulateAspectAsync<ContentItemMetadata>(contentItem)).AdminRouteValues;

            if (!string.IsNullOrEmpty(returnUrl))
            {
                adminRouteValues.Add("returnUrl", returnUrl);
            }

            return RedirectToRoute(adminRouteValues);
        }

        public async Task<IActionResult> Display(string contentItemId)
        {
            var contentItem = await _contentManager.GetAsync(contentItemId, VersionOptions.Latest);

            if (contentItem == null)
            {
                return NotFound();
            }

            if (!await _authorizationService.AuthorizeAsync(User, Permissions.ViewContent, contentItem))
            {
                return Forbid();
            }

            var model = await _contentItemDisplayManager.BuildDisplayAsync(contentItem, _updateModelAccessor.ModelUpdater, "DetailAdmin");

            return View(model);
        }

        public async Task<IActionResult> Edit(string contentItemId)
        {
            var contentItem = await _contentManager.GetAsync(contentItemId, VersionOptions.Latest);

            if (contentItem == null)
                return NotFound();

            if (!await _authorizationService.AuthorizeAsync(User, Permissions.EditContent, contentItem))
            {
                return Forbid();
            }

            var model = await _contentItemDisplayManager.BuildEditorAsync(contentItem, _updateModelAccessor.ModelUpdater, false);

            return View(model);
        }

        [HttpPost, ActionName("Edit")]
        [FormValueRequired("submit.Save")]
        public Task<IActionResult> EditPOST(string contentItemId, [Bind(Prefix = "submit.Save")] string submitSave, string returnUrl)
        {
            var stayOnSamePage = submitSave == "submit.SaveAndContinue";
            return EditPOST(contentItemId, returnUrl, stayOnSamePage, contentItem =>
            {
                var typeDefinition = _contentDefinitionManager.GetTypeDefinition(contentItem.ContentType);

                _notifier.Success(string.IsNullOrWhiteSpace(typeDefinition.DisplayName)
                    ? H["Your content draft has been saved."]
                    : H["Your {0} draft has been saved.", typeDefinition.DisplayName]);

                return Task.CompletedTask;
            });
        }

        [HttpPost, ActionName("Edit")]
        [FormValueRequired("submit.Publish")]
        public async Task<IActionResult> EditAndPublishPOST(string contentItemId, [Bind(Prefix = "submit.Publish")] string submitPublish, string returnUrl)
        {
            var stayOnSamePage = submitPublish == "submit.PublishAndContinue";

            var content = await _contentManager.GetAsync(contentItemId, VersionOptions.Latest);

            if (content == null)
            {
                return NotFound();
            }

            if (!await _authorizationService.AuthorizeAsync(User, Permissions.PublishContent, content))
            {
                return Forbid();
            }
            return await EditPOST(contentItemId, returnUrl, stayOnSamePage, async contentItem =>
            {
                await _contentManager.PublishAsync(contentItem);

                var typeDefinition = _contentDefinitionManager.GetTypeDefinition(contentItem.ContentType);

                _notifier.Success(string.IsNullOrWhiteSpace(typeDefinition.DisplayName)
                    ? H["Your content has been published."]
                    : H["Your {0} has been published.", typeDefinition.DisplayName]);
            });
        }

        private async Task<IActionResult> EditPOST(string contentItemId, string returnUrl, bool stayOnSamePage, Func<ContentItem, Task> conditionallyPublish)
        {
            var contentItem = await _contentManager.GetAsync(contentItemId, VersionOptions.DraftRequired);

            if (contentItem == null)
            {
                return NotFound();
            }

            if (!await _authorizationService.AuthorizeAsync(User, Permissions.EditContent, contentItem))
            {
                return Forbid();
            }

            //string previousRoute = null;
            //if (contentItem.Has<IAliasAspect>() &&
            //    !string.IsNullOrWhiteSpace(returnUrl)
            //    && Request.IsLocalUrl(returnUrl)
            //    // only if the original returnUrl is the content itself
            //    && String.Equals(returnUrl, Url.ItemDisplayUrl(contentItem), StringComparison.OrdinalIgnoreCase)
            //    )
            //{
            //    previousRoute = contentItem.As<IAliasAspect>().Path;
            //}

            var model = await _contentItemDisplayManager.UpdateEditorAsync(contentItem, _updateModelAccessor.ModelUpdater, false);
            if (!ModelState.IsValid)
            {
                _session.Cancel();
                return View("Edit", model);
            }

            // The content item needs to be marked as saved in case the drivers or the handlers have
            // executed some query which would flush the saved entities inside the above UpdateEditorAsync.
            _session.Save(contentItem);

            await conditionallyPublish(contentItem);

            if (returnUrl == null)
            {
                return RedirectToAction("Edit", new RouteValueDictionary { { "ContentItemId", contentItem.ContentItemId } });
            }
            else if (stayOnSamePage)
            {
                return RedirectToAction("Edit", new RouteValueDictionary { { "ContentItemId", contentItem.ContentItemId }, { "returnUrl", returnUrl } });
            }
            else
            {
                return LocalRedirect(returnUrl);
            }
        }

        [HttpPost]
        public async Task<IActionResult> Clone(string contentItemId, string returnUrl)
        {
            var contentItem = await _contentManager.GetAsync(contentItemId, VersionOptions.Latest);

            if (contentItem == null)
                return NotFound();

            if (!await _authorizationService.AuthorizeAsync(User, Permissions.CloneContent, contentItem))
            {
                return Forbid();
            }

            try
            {
                await _contentManager.CloneAsync(contentItem);
            }
            catch (InvalidOperationException)
            {
                _notifier.Warning(H["Could not clone the content item."]);
                return Url.IsLocalUrl(returnUrl) ? (IActionResult)LocalRedirect(returnUrl) : RedirectToAction("List");
            }

            _notifier.Information(H["Successfully cloned. The clone was saved as a draft."]);

            return Url.IsLocalUrl(returnUrl) ? (IActionResult)LocalRedirect(returnUrl) : RedirectToAction("List");
        }

        [HttpPost]
        public async Task<IActionResult> DiscardDraft(string contentItemId, string returnUrl)
        {
            var contentItem = await _contentManager.GetAsync(contentItemId, VersionOptions.Latest);

            if (contentItem == null || contentItem.Published)
            {
                return NotFound();
            }

            if (!await _authorizationService.AuthorizeAsync(User, Permissions.DeleteContent, contentItem))
            {
                return Forbid();
            }

            if (contentItem != null)
            {
                var typeDefinition = _contentDefinitionManager.GetTypeDefinition(contentItem.ContentType);

                await _contentManager.DiscardDraftAsync(contentItem);

                _notifier.Success(string.IsNullOrWhiteSpace(typeDefinition.DisplayName)
                    ? H["The draft has been removed."]
                    : H["The {0} draft has been removed.", typeDefinition.DisplayName]);
            }

            return Url.IsLocalUrl(returnUrl) ? (IActionResult)LocalRedirect(returnUrl) : RedirectToAction("List");
        }

        [HttpPost]
        public async Task<IActionResult> Remove(string contentItemId, string returnUrl)
        {
            var contentItem = await _contentManager.GetAsync(contentItemId, VersionOptions.Latest);

            if (!await _authorizationService.AuthorizeAsync(User, Permissions.DeleteContent, contentItem))
            {
                return Forbid();
            }

            if (contentItem != null)
            {
                var typeDefinition = _contentDefinitionManager.GetTypeDefinition(contentItem.ContentType);

                await _contentManager.RemoveAsync(contentItem);

                _notifier.Success(string.IsNullOrWhiteSpace(typeDefinition.DisplayName)
                    ? H["That content has been removed."]
                    : H["That {0} has been removed.", typeDefinition.DisplayName]);
            }

            return Url.IsLocalUrl(returnUrl) ? (IActionResult)LocalRedirect(returnUrl) : RedirectToAction("List");
        }

        [HttpPost]
        public async Task<IActionResult> Publish(string contentItemId, string returnUrl)
        {
            var contentItem = await _contentManager.GetAsync(contentItemId, VersionOptions.Latest);
            if (contentItem == null)
            {
                return NotFound();
            }

            if (!await _authorizationService.AuthorizeAsync(User, Permissions.PublishContent, contentItem))
            {
                return Forbid();
            }

            await _contentManager.PublishAsync(contentItem);

            var typeDefinition = _contentDefinitionManager.GetTypeDefinition(contentItem.ContentType);

            if (string.IsNullOrEmpty(typeDefinition.DisplayName))
            {
                _notifier.Success(H["That content has been published."]);
            }
            else
            {
                _notifier.Success(H["That {0} has been published.", typeDefinition.DisplayName]);
            }

            return Url.IsLocalUrl(returnUrl) ? (IActionResult)LocalRedirect(returnUrl) : RedirectToAction("List");
        }

        [HttpPost]
        public async Task<IActionResult> Unpublish(string contentItemId, string returnUrl)
        {
            var contentItem = await _contentManager.GetAsync(contentItemId, VersionOptions.Latest);
            if (contentItem == null)
            {
                return NotFound();
            }

            if (!await _authorizationService.AuthorizeAsync(User, Permissions.PublishContent, contentItem))
            {
                return Forbid();
            }

            await _contentManager.UnpublishAsync(contentItem);

            var typeDefinition = _contentDefinitionManager.GetTypeDefinition(contentItem.ContentType);

            if (string.IsNullOrEmpty(typeDefinition.DisplayName))
            {
                _notifier.Success(H["The content has been unpublished."]);
            }
            else
            {
                _notifier.Success(H["The {0} has been unpublished.", typeDefinition.DisplayName]);
            }

            return Url.IsLocalUrl(returnUrl) ? (IActionResult)LocalRedirect(returnUrl) : RedirectToAction("List");
        }

        private ValueTask<IShape> CreateZoneShapeAsync(string actualShapeType)
        {
            return _shapeFactory.CreateAsync(actualShapeType, () =>
                new ValueTask<IShape>(new ZoneHolding(() => _shapeFactory.CreateAsync("Zone"))));
        }

        private static async Task AddToZone(string zoneName, dynamic zones, IShape shape, string position)
        {
            var zone = zones.Zones[zoneName];
            if (zone is ZoneOnDemand zoneOnDemand)
            {
                await zoneOnDemand.AddAsync(shape, position);
            }
            else if (zone is Shape zoneShape)
            {
                zoneShape.Add(shape, position);
            }
        }

        private static Action<ContentOptionsViewModel> BuildContentOptionsViewModel(ContentOptionsViewModel model)
        {
            return m =>
            {
                m.ContentTypeOptions = model.ContentTypeOptions;
                m.ContentStatuses = model.ContentStatuses;
                m.ContentSorts = model.ContentSorts;
                m.ContentsBulkAction = model.ContentsBulkAction;
                m.CreatableTypes = model.CreatableTypes;
            };
        }
    }
}
