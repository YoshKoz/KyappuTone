using KyappuTone.Core.Library;
using Shouldly;
using Xunit;

namespace KyappuTone.Core.Tests.Library;

public class FolderPlaylistPlannerTests
{
    [Fact]
    public void Creates_one_playlist_per_immediate_subfolder()
    {
        var roots = new[]
        {
            new LibraryRoot("Main", @"C:\Music\Main"),
            new LibraryRoot("Other", @"C:\Music\Other"),
        };
        var folders = new[]
        {
            @"C:\Music\Main\Rock",
            @"C:\Music\Other\Jazz",
        };

        var playlists = FolderPlaylistPlanner.Build(roots, folders);

        playlists.Count.ShouldBe(2);
        playlists.ShouldContain(p => p.FolderPath == @"C:\Music\Main\Rock" && p.Name == "Rock");
        playlists.ShouldContain(p => p.FolderPath == @"C:\Music\Other\Jazz" && p.Name == "Jazz");
    }

    [Fact]
    public void Uses_library_prefix_when_folder_names_collide()
    {
        var roots = new[]
        {
            new LibraryRoot("Main", @"C:\Music\Main"),
            new LibraryRoot("Other", @"C:\Music\Other"),
        };
        var folders = new[]
        {
            @"C:\Music\Main\Rock",
            @"C:\Music\Other\Rock",
        };

        var playlists = FolderPlaylistPlanner.Build(roots, folders);

        playlists.Count.ShouldBe(2);
        playlists.ShouldContain(p => p.Name == "Main - Rock");
        playlists.ShouldContain(p => p.Name == "Other - Rock");
    }

    [Fact]
    public void Ignores_folders_outside_the_library_root()
    {
        var roots = new[] { new LibraryRoot("Main", @"C:\Music\Main") };
        var folders = new[]
        {
            @"C:\Music\Main\Rock",
            @"C:\Music\Main\Rock\Live",
            @"C:\Elsewhere\Jazz",
        };

        var playlists = FolderPlaylistPlanner.Build(roots, folders);

        playlists.Count.ShouldBe(1);
        playlists[0].FolderPath.ShouldBe(@"C:\Music\Main\Rock");
    }

    [Fact]
    public void Is_case_insensitive_on_windows_paths()
    {
        var roots = new[] { new LibraryRoot("Main", @"C:\Music\Main") };
        var folders = new[] { @"c:\music\main\rock" };

        var playlists = FolderPlaylistPlanner.Build(roots, folders);

        playlists.Count.ShouldBe(1);
        playlists[0].Name.ShouldBe("rock");
    }
}
