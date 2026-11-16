namespace lively.Tests;

public class LibraryViewModelTests
{
    [Fact]
    public void Scan_LoadsWallpaperFoldersSortedByTitle()
    {
        using var tmp = new TempDirectory();
        WallpaperFixture.Create(tmp, "1", "Waves");
        WallpaperFixture.Create(tmp, "2", "Aurora");
        WallpaperFixture.Create(tmp, "3", "Rain");

        var vm = new LibraryViewModel([tmp.Path]);

        Assert.Equal(["Aurora", "Rain", "Waves"], vm.LibraryItems.Select(x => x.Title));
    }

    [Fact]
    public void Scan_SkipsFoldersWithoutValidLivelyInfo()
    {
        using var tmp = new TempDirectory();
        WallpaperFixture.Create(tmp, "valid", "Rain");
        tmp.CreateDirectory("not-a-wallpaper");
        tmp.WriteFile("{ not json", "corrupt", "LivelyInfo.json");

        var vm = new LibraryViewModel([tmp.Path]);

        Assert.Equal(["Rain"], vm.LibraryItems.Select(x => x.Title));
    }

    [Fact]
    public void Scan_MissingFolder_GivesEmptyLibrary()
    {
        using var tmp = new TempDirectory();

        var vm = new LibraryViewModel([tmp.Combine("does-not-exist")]);

        Assert.Empty(vm.LibraryItems);
    }

    [Fact]
    public void AddWallpaper_InsertsInTitleOrder()
    {
        using var tmp = new TempDirectory();
        using var other = new TempDirectory();
        WallpaperFixture.Create(tmp, "1", "Aurora");
        WallpaperFixture.Create(tmp, "2", "Waves");
        var vm = new LibraryViewModel([tmp.Path]);

        vm.AddWallpaper(WallpaperFixture.Create(other, "new", "Rain"));

        Assert.Equal(["Aurora", "Rain", "Waves"], vm.LibraryItems.Select(x => x.Title));
    }

    [Fact]
    public void SelectedItem_StoresValueAndNotifies()
    {
        using var tmp = new TempDirectory();
        WallpaperFixture.Create(tmp, "1", "Rain");
        var vm = new LibraryViewModel([tmp.Path]);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.SelectedItem = vm.LibraryItems[0];

        Assert.Same(vm.LibraryItems[0], vm.SelectedItem);
        Assert.Equal([nameof(LibraryViewModel.SelectedItem)], changed);
    }
}
