using CommunityToolkit.WinUI;
using Files.App.Extensions;
using Files.App.Filesystem;
using Files.App.Filesystem.StorageItems;
using Files.App.Helpers;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace Files.App.ViewModels.Properties
{
    internal class CombinedProperties : BaseProperties
    {
        public List<ListedItem> List { get; }

        public CombinedProperties(SelectedItemsPropertiesViewModel viewModel, CancellationTokenSource tokenSource,
            DispatcherQueue coreDispatcher, List<ListedItem> listedItems, IShellPage instance)
        {
            ViewModel = viewModel;
            TokenSource = tokenSource;
            Dispatcher = coreDispatcher;
            List = listedItems;
            AppInstance = instance;
            GetBaseProperties();
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        public override void GetBaseProperties()
        {
            if (List is not null)
            {
                ViewModel.LoadCombinedItemsGlyph = true;
                if (List.All(x => x.ItemType.Equals(List.First().ItemType)))
                {
                    ViewModel.ItemType = string.Format("PropertiesDriveItemTypesEquals".GetLocalizedResource(), List.First().ItemType);
                }
                else
                {
                    ViewModel.ItemType = "PropertiesDriveItemTypeDifferent".GetLocalizedResource();
                }
                var itemsPath = List.Select(Item => (Item as RecycleBinItem)?.ItemOriginalFolder ??
                    (Path.IsPathRooted(Item.ItemPath) ? Path.GetDirectoryName(Item.ItemPath) : Item.ItemPath));
                if (itemsPath.Distinct().Count() == 1)
                {
                    ViewModel.ItemPath = string.Format("PropertiesCombinedItemPath".GetLocalizedResource(), itemsPath.First());
                }
            }
        }

        public override async void GetSpecialProperties()
        {
            if (List.All(x => x.PrimaryItemAttribute == StorageItemTypes.File))
            {
                ViewModel.IsReadOnly = List.All(x => NativeFileOperationsHelper.HasFileAttribute(x.ItemPath, System.IO.FileAttributes.ReadOnly));
            }
            ViewModel.IsHidden = List.All(x => NativeFileOperationsHelper.HasFileAttribute(x.ItemPath, System.IO.FileAttributes.Hidden));

            ViewModel.LastSeparatorVisibility = false;
            ViewModel.ItemSizeVisibility = true;

            ViewModel.FilesCount += List.Where(x => x.PrimaryItemAttribute == StorageItemTypes.File).ToList().Count;
            ViewModel.FoldersCount += List.Where(x => x.PrimaryItemAttribute == StorageItemTypes.Folder).ToList().Count;

            long totalSize = 0;
            long filesSize = List.Where(x => x.PrimaryItemAttribute == StorageItemTypes.File).Sum(x => x.FileSizeBytes);
            long foldersSize = 0;

            ViewModel.ItemSizeProgressVisibility = true;
            foreach (var item in List)
            {
                if (item.PrimaryItemAttribute == StorageItemTypes.Folder)
                {
                    var fileSizeTask = Task.Run(async () =>
                    {
                        var size = await CalculateFolderSizeAsync(item.ItemPath, TokenSource.Token);
                        return size;
                    });
                    try
                    {
                        foldersSize += await fileSizeTask;
                    }
                    catch (Exception ex)
                    {
                        App.Logger.Warn(ex, ex.Message);
                    }
                }
            }
            ViewModel.ItemSizeProgressVisibility = false;

            totalSize = filesSize + foldersSize;
            ViewModel.ItemSize = totalSize.ToLongSizeString();
            SetItemsCountString();
        }

        public async void GetSystemFileProperties()
        {
            List<BaseStorageFile> files = new();
            foreach (ListedItem Item in List)
            {
                BaseStorageFile file = await FilesystemTasks.Wrap(() => StorageFileExtensions.DangerousGetFileFromPathAsync(Item.ItemPath));
                if (file == null)
                {
                    // Could not access file, can't show any other property
                    continue;
                }
                files.Add(file);
            }



            List<List<FileProperty>> listAll = new();
            foreach (BaseStorageFile file in files)
            {
                var listItem = await FileProperty.RetrieveAndInitializePropertiesAsync(file);
                listAll.Add(listItem);
            }



            List<List<FileProperty>> precombinded = new();
            foreach (List<FileProperty> list in listAll)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (precombinded.ElementAtOrDefault(i) == null)
                    {
                        List<FileProperty> tempList = new()
                        {
                            list[i]
                        };
                        precombinded.Add(tempList);
                    }
                    else
                    {
                        precombinded[i].Add(list[i]);
                    }
                }
            }

            //precombinded.Where(x => x.Property == "System.ItemTypeText").Sum(x => x.FileSizeBytes);

            List<FileProperty> finalProperties = new();
            foreach (List<FileProperty> list in precombinded)
            {
                if (list.All(x => x.Value == null))
                {
                    finalProperties.Add(list.First());
                    continue;
                }
                switch (list.First().Property)
                {
                    case "System.ItemTypeText":
                        if (!list.All(x => x.Value.Equals(list.First().Value)))
                        {
                            list.First().Value = string.Join("; ", list.Select(x => x.Value).Distinct().ToList());
                            finalProperties.Add(list.First());
                        }
                        else
                        {
                            finalProperties.Add(list.First());
                        }
                        break;

                    case "System.DateModified":
                        finalProperties.Add(list.First());
                        break;

                    case "System.Media.Duration":
                        if (!list.All(x => x.Value == null))
                        {
                            list.First().Value = list.Select(x => x.Value).ToList().Sum(str => Convert.ToInt64(str));
                            finalProperties.Add(list.First());
                        }
                        break;

                    case "System.FilePlaceholderStatus":
                        finalProperties.Add(list.First());
                        break;

                    default:
                        if (list.Where(x => x.Value != null).Count() >= 1)
                        {
                            var result = list.Where(x => x.Value != null);
                            if ((result.Any() && result.Count() == 1) || result.All(x => x.Value.Equals(result.First().Value)))
                            {
                                finalProperties.Add(result.First());
                            }
                            else
                            {
                                result.First().Value = "PropertiesFilesHasMultipleValues".GetLocalizedResource();
                                finalProperties.Add(result.First());
                            }
                        }
                        else if (!list.All(x => x.Value.Equals(list.First().Value)))
                        {
                            list.First().Value = "PropertiesFilesHasMultipleValues".GetLocalizedResource();
                            finalProperties.Add(list.First());
                        }
                        else
                        {
                            finalProperties.Add(list.First());
                        }
                        break;
                }
                // System.ItemPathDisplay
            }

            var query = finalProperties
                .Where(fileProp => !(fileProp.Value == null && fileProp.IsReadOnly))
                .GroupBy(fileProp => fileProp.SectionResource)
                .Select(group => new FilePropertySection(group) { Key = group.Key })
                .Where(section => !section.All(fileProp => fileProp.Value == null))
                .OrderBy(group => group.Priority);
            ViewModel.PropertySections = new ObservableCollection<FilePropertySection>(query);
            ViewModel.FileProperties = new ObservableCollection<FileProperty>(finalProperties.Where(i => i.Value != null));
        }

        private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "IsReadOnly":
                    if (ViewModel.IsReadOnly)
                    {
                        List.ForEach(x => NativeFileOperationsHelper.SetFileAttribute(
                            x.ItemPath, System.IO.FileAttributes.ReadOnly));
                    }
                    else
                    {
                        List.ForEach(x => NativeFileOperationsHelper.UnsetFileAttribute(
                            x.ItemPath, System.IO.FileAttributes.ReadOnly));
                    }
                    break;

                case "IsHidden":
                    if (ViewModel.IsHidden)
                    {
                        List.ForEach(x => NativeFileOperationsHelper.SetFileAttribute(
                            x.ItemPath, System.IO.FileAttributes.Hidden));
                    }
                    else
                    {
                        List.ForEach(x => NativeFileOperationsHelper.UnsetFileAttribute(
                            x.ItemPath, System.IO.FileAttributes.Hidden));
                    }
                    break;
            }
        }
    }
}