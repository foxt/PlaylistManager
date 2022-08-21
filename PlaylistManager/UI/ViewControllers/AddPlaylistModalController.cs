﻿using System;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using static BeatSaberMarkupLanguage.Components.CustomListTableData;
using System.Reflection;
using System.Linq;
using HMUI;
using PlaylistManager.Utilities;
using UnityEngine;
using PlaylistManager.Configuration;
using BeatSaberPlaylistsLib.Types;
using BeatSaberMarkupLanguage.Parser;
using System.IO;
using System.ComponentModel;
using System.Collections.Generic;
using System.Threading.Tasks;
using PlaylistManager.Services;

namespace PlaylistManager.UI
{
    internal class AddPlaylistModalController : INotifyPropertyChanged
    {
        private readonly StandardLevelDetailViewController standardLevelDetailViewController;
        private readonly PopupModalsController popupModalsController;
        private readonly PlaylistCreationService playlistCreationService;

        private BeatSaberPlaylistsLib.PlaylistManager parentManager;
        private List<BeatSaberPlaylistsLib.PlaylistManager> childManagers;
        private List<IPlaylist> childPlaylists;

        private readonly Sprite folderIcon;
        private bool parsed;
        public event PropertyChangedEventHandler PropertyChanged;

        [UIComponent("list")]
        public CustomListTableData playlistTableData;

        [UIComponent("dropdown-options")]
        public CustomListTableData dropdownTableData;

        [UIComponent("highlight-checkbox")]
        private readonly RectTransform highlightCheckboxTransform;

        [UIComponent("modal")]
        private readonly RectTransform modalTransform;

        private Vector3 modalPosition;

        [UIComponent("create-dropdown")]
        private ModalView createModal;

        [UIComponent("create-dropdown")]
        private readonly RectTransform createModalTransform;

        private Vector3 createModalPosition;

        [UIParams]
        private readonly BSMLParserParams parserParams;

        public AddPlaylistModalController(StandardLevelDetailViewController standardLevelDetailViewController, PopupModalsController popupModalsController, PlaylistCreationService playlistCreationService)
        {
            this.standardLevelDetailViewController = standardLevelDetailViewController;
            this.popupModalsController = popupModalsController;
            this.playlistCreationService = playlistCreationService;
            folderIcon = BeatSaberMarkupLanguage.Utilities.FindSpriteInAssembly("PlaylistManager.Icons.FolderIcon.png");
            parsed = false;
        }

        private void Parse()
        {
            if (!parsed)
            {
                BSMLParser.instance.Parse(BeatSaberMarkupLanguage.Utilities.GetResourceContent(Assembly.GetExecutingAssembly(), "PlaylistManager.UI.Views.AddPlaylistModal.bsml"), standardLevelDetailViewController.transform.Find("LevelDetail").gameObject, this);
                modalPosition = modalTransform.localPosition;
                createModalPosition = createModalTransform.localPosition;
            }
            modalTransform.localPosition = modalPosition; // Reset position
            createModalTransform.localPosition = createModalPosition;
        }

        [UIAction("#post-parse")]
        private void PostParse()
        {
            parsed = true;
            highlightCheckboxTransform.transform.localScale *= 0.5f;

            Accessors.AnimateCanvasAccessor(ref createModal) = false;
            dropdownTableData.data.Add(new CustomCellInfo("Playlist"));
            dropdownTableData.data.Add(new CustomCellInfo("Folder"));
            dropdownTableData.tableView.ReloadData();
        }

        #region Show Playlists

        internal void ShowModal()
        {
            Parse();
            parserParams.EmitEvent("close-modal");
            parserParams.EmitEvent("open-modal");
            ShowPlaylistsForManager(BeatSaberPlaylistsLib.PlaylistManager.DefaultManager);
        }

        internal void ShowPlaylistsForManager(BeatSaberPlaylistsLib.PlaylistManager parentManager)
        {
            playlistTableData.data.Clear();

            this.parentManager = parentManager;
            childManagers = parentManager.GetChildManagers().ToList();
            var childPlaylists = parentManager.GetAllPlaylists(false).Where(playlist => !playlist.ReadOnly);
            this.childPlaylists = childPlaylists.ToList();

            foreach (var playlistManager in childManagers)
            {
                playlistTableData.data.Add(new CustomCellInfo(Path.GetFileName(playlistManager.PlaylistPath), "Folder", folderIcon));
            }
            foreach (var playlist in childPlaylists)
            {
                if (playlist is IStagedSpriteLoad stagedSpriteLoadPlaylist && !stagedSpriteLoadPlaylist.SmallSpriteWasLoaded)
                {
                    stagedSpriteLoadPlaylist.SpriteLoaded -= StagedSpriteLoadPlaylist_SpriteLoaded;
                    stagedSpriteLoadPlaylist.SpriteLoaded += StagedSpriteLoadPlaylist_SpriteLoaded;
                    _ = playlist.smallCoverImage;
                }
                else
                {
                    ShowPlaylist(playlist);
                }
            }
            playlistTableData.tableView.ReloadData();

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BackActive)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FolderText)));
        }

        private void StagedSpriteLoadPlaylist_SpriteLoaded(object sender, EventArgs e)
        {
            if (sender is IStagedSpriteLoad stagedSpriteLoadPlaylist)
            {
                if (parentManager.GetAllPlaylists(false).Contains((IPlaylist)stagedSpriteLoadPlaylist))
                {
                    ShowPlaylist((IPlaylist)stagedSpriteLoadPlaylist);
                }
                playlistTableData.tableView.ReloadDataKeepingPosition();
                (stagedSpriteLoadPlaylist).SpriteLoaded -= StagedSpriteLoadPlaylist_SpriteLoaded;
            }
        }

        private void ShowPlaylist(IPlaylist playlist)
        {
            var subName = string.Format("{0} songs", playlist.beatmapLevelCollection.beatmapLevels.Count);
            if (playlist.beatmapLevelCollection.beatmapLevels.Any(level => level.levelID == standardLevelDetailViewController.selectedDifficultyBeatmap.level.levelID))
            {
                if (!playlist.AllowDuplicates)
                {
                    childPlaylists.Remove(playlist);
                    return;
                }
                subName += " (contains song)";
            }
            playlistTableData.data.Add(new CustomCellInfo(playlist.collectionName, subName, playlist.smallCoverImage));
        }

        [UIAction("select-cell")]
        private void OnCellSelect(TableView tableView, int index)
        {
            playlistTableData.tableView.ClearSelection();
            // Folder Selected
            if (index < childManagers.Count)
            {
                ShowPlaylistsForManager(childManagers[index]);
            }
            else
            {
                index -= childManagers.Count;
                var selectedPlaylist = childPlaylists[index];
                IPlaylistSong playlistSong;
                if (HighlightDifficulty)
                {
                    playlistSong = selectedPlaylist.Add(standardLevelDetailViewController.selectedDifficultyBeatmap);
                }
                else
                {
                    playlistSong = selectedPlaylist.Add(standardLevelDetailViewController.selectedDifficultyBeatmap.level);
                }
                try
                {
                    parentManager.StorePlaylist(selectedPlaylist);
                    popupModalsController.ShowOkModal(modalTransform, string.Format("Song successfully added to {0}", selectedPlaylist.collectionName), null, animateParentCanvas: false);
                    Events.RaisePlaylistSongAdded(playlistSong, selectedPlaylist);
                }
                catch (Exception e)
                {
                    popupModalsController.ShowOkModal(modalTransform, "An error occured while adding song to playlist.", null, animateParentCanvas: false);
                    Plugin.Log.Critical(string.Format("An exception was thrown while adding a song to a playlist.\nException Message: {0}", e.Message));
                }
                finally
                {
                    ShowPlaylistsForManager(parentManager);
                }
            }
        }

        [UIAction("back-button-pressed")]
        private void BackButtonPressed()
        {
            ShowPlaylistsForManager(parentManager.Parent);
        }

        [UIValue("folder-text")]
        private string FolderText
        {
            get => parentManager == null ? "" : Path.GetFileName(parentManager.PlaylistPath);
        }

        [UIValue("highlight-difficulty")]
        private bool HighlightDifficulty
        {
            get => PluginConfig.Instance.HighlightDifficulty;
            set
            {
                PluginConfig.Instance.HighlightDifficulty = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HighlightDifficulty)));
            }
        }

        [UIValue("back-active")]
        private bool BackActive
        {
            get => parentManager != null && parentManager.Parent != null;
        }

        #endregion

        #region Create Playlist

        [UIAction("select-option")]
        private void OnOptionSelect(TableView tableView, int index)
        {
            popupModalsController.ShowKeyboard(modalTransform,
                index == 0 ? playlistName => _ = CreatePlaylistAsync(playlistName) : CreateFolder,
                animateParentCanvas: false);
            tableView.ClearSelection();
            parserParams.EmitEvent("close-dropdown");
        }

        private async Task CreatePlaylistAsync(string playlistName)
        {
            if (string.IsNullOrWhiteSpace(playlistName))
            {
                return;
            }

            var playlist = await playlistCreationService.CreatePlaylistAsync(playlistName, parentManager);

            if (playlist is IDeferredSpriteLoad {SpriteWasLoaded: false} deferredSpriteLoadPlaylist)
            {
                deferredSpriteLoadPlaylist.SpriteLoaded -= StagedSpriteLoadPlaylist_SpriteLoaded;
                deferredSpriteLoadPlaylist.SpriteLoaded += StagedSpriteLoadPlaylist_SpriteLoaded;
                _ = playlist.coverImage;
            }

            childPlaylists.Add(playlist);
            playlistTableData.tableView.ReloadDataKeepingPosition();
        }

        private void CreateFolder(string folderName)
        {
            folderName = folderName.Replace("/", "").Replace("\\", "").Replace(".", "");
            if (!string.IsNullOrEmpty(folderName))
            {
                var childManager = parentManager.CreateChildManager(folderName);

                if (childManagers.Contains(childManager))
                {
                    popupModalsController.ShowOkModal(modalTransform, "\"" + folderName + "\" already exists! Please use a different name.", null, animateParentCanvas: false);
                }
                else
                {
                    playlistTableData.data.Insert(childManagers.Count, new CustomCellInfo(Path.GetFileName(childManager.PlaylistPath), "Folder", folderIcon));
                    playlistTableData.tableView.ReloadDataKeepingPosition();
                    childManagers.Add(childManager);
                    BeatSaberPlaylistsLib.PlaylistManager.DefaultManager.RequestRefresh("PlaylistManager (plugin)");
                }
            }
        }

        #endregion
    }
}
