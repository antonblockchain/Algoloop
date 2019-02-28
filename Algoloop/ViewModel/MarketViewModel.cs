﻿/*
 * Copyright 2018 Capnode AB
 * 
 * Licensed under the Apache License, Version 2.0 (the "License"); 
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using Algoloop.Common;
using Algoloop.Lean;
using Algoloop.Model;
using Algoloop.Provider;
using Algoloop.ViewSupport;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using Microsoft.Win32;
using QuantConnect.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Algoloop.ViewModel
{
    public class MarketViewModel : ViewModelBase, ITreeViewModel
    {
        private readonly MarketsViewModel _parent;
        private readonly SettingsModel _settingsModel;
        private CancellationTokenSource _cancel;
        private MarketModel _model;
        private Isolated<ProviderFactory> _factory;
        private SymbolViewModel _selectedSymbol;
        private ObservableCollection<DataGridColumn> _symbolColumns = new ObservableCollection<DataGridColumn>();
        private Style _rightCellStyle = new Style(typeof(TextBlock));
        private Style _leftCellStyle = new Style(typeof(TextBlock));

        public MarketViewModel(MarketsViewModel marketsViewModel, MarketModel marketModel, SettingsModel settingsModel)
        {
            _parent = marketsViewModel;
            Model = marketModel;
            _settingsModel = settingsModel;

            AddSymbolCommand = new RelayCommand(() => AddSymbol(), () => !_parent.IsBusy);
            AddAllSymbolsCommand = new RelayCommand(() => AddAllSymbols(), !_parent.IsBusy);
            DeleteSymbolsCommand = new RelayCommand<IList>(m => DeleteSymbols(m), m => !_parent.IsBusy && !Active && SelectedSymbol != null);
            ImportSymbolsCommand = new RelayCommand(() => ImportSymbols(), !_parent.IsBusy);
            ExportSymbolsCommand = new RelayCommand<IList>(m => ExportSymbols(m), m => !_parent.IsBusy && !Active && SelectedSymbol != null);
            DeleteCommand = new RelayCommand(() => _parent?.DeleteMarket(this), () => !_parent.IsBusy && !Active);
            NewListCommand = new RelayCommand(() => Folders.Add(new FolderViewModel(this, new FolderModel())), () => !_parent.IsBusy && !Active);
            ActiveCommand = new RelayCommand(() => OnActiveCommand(Model.Active), !_parent.IsBusy);
            StartCommand = new RelayCommand(() => OnStartCommand(), () => !_parent.IsBusy && !Active);
            StopCommand = new RelayCommand(() => OnStopCommand(), () => !_parent.IsBusy && Active);

            _rightCellStyle.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
            _leftCellStyle.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Left));

            DataFromModel();
            OnActiveCommand(Active);
        }

        public RelayCommand<IList> SymbolSelectionChangedCommand { get; }
        public RelayCommand AddSymbolCommand { get; }
        public RelayCommand AddAllSymbolsCommand { get; }
        public RelayCommand<IList> DeleteSymbolsCommand { get; }
        public RelayCommand ImportSymbolsCommand { get; }
        public RelayCommand<IList> ExportSymbolsCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public RelayCommand NewListCommand { get; }
        public RelayCommand ActiveCommand { get; }
        public RelayCommand StartCommand { get; }
        public RelayCommand StopCommand { get; }

        public SyncObservableCollection<SymbolViewModel> Symbols { get; } = new SyncObservableCollection<SymbolViewModel>();
        public SyncObservableCollection<FolderViewModel> Folders { get; } = new SyncObservableCollection<FolderViewModel>();
        public string DataFolder => _settingsModel.DataFolder;

        public bool Active
        {
            get => Model.Active;
            set
            {
                Model.Active = value;
                RaisePropertyChanged(() => Active);
                StartCommand.RaiseCanExecuteChanged();
                StopCommand.RaiseCanExecuteChanged();
                DeleteCommand.RaiseCanExecuteChanged();
                DeleteSymbolsCommand.RaiseCanExecuteChanged();
                ExportSymbolsCommand.RaiseCanExecuteChanged();
            }
        }

        public MarketModel Model
        {
            get => _model;
            set => Set(ref _model, value);
        }

        public SymbolViewModel SelectedSymbol
        {
            get => _selectedSymbol;
            set
            {
                Set(ref _selectedSymbol, value);
                DeleteSymbolsCommand.RaiseCanExecuteChanged();
                ExportSymbolsCommand.RaiseCanExecuteChanged();
            }
        }

        public ObservableCollection<DataGridColumn> SymbolColumns
        {
            get => _symbolColumns;
            set => Set(ref _symbolColumns, value);
        }

        public void Refresh()
        {
            Model.Refresh();
            foreach (FolderViewModel folder in Folders)
            {
                folder.Refresh();
            }
        }

        internal void DataToModel()
        {
            Model.Symbols.Clear();
            foreach (SymbolViewModel symbol in Symbols)
            {
                Model.Symbols.Add(symbol.Model);
            }

            Model.Folders.Clear();
            foreach (FolderViewModel folder in Folders)
            {
                Model.Folders.Add(folder.Model);
                folder.DataToModel();
            }
        }

        internal void DataFromModel()
        {
            SymbolColumns.Clear();
            SymbolColumns.Add(new DataGridCheckBoxColumn()
            {
                Header = "Download",
                Binding = new Binding("Active") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }
            });
            SymbolColumns.Add(new DataGridTextColumn()
            {
                Header = "Name",
                Binding = new Binding("Model.Name") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }
            });

            Active = Model.Active;
            Symbols.Clear();
            Model.Symbols.Sort();
            foreach (SymbolModel symbolModel in Model.Symbols)
            {
                var symbolViewModel = new SymbolViewModel(this, symbolModel);
                Symbols.Add(symbolViewModel);

                if (symbolModel.Properties == null)
                    continue;

                foreach (var property in symbolModel.Properties)
                {
                    if (!SymbolColumns.Any(m => m.Header.Equals(property.Key)))
                    {
                        bool isDecimal = decimal.TryParse(property.Value, out _);
                        SymbolColumns.Add(new DataGridTextColumn()
                        {
                            Header = property.Key,
                            IsReadOnly = true,
                            Binding = new Binding($"Model.Properties[{property.Key}]")
                            {
                                Mode = BindingMode.OneWay,
                                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                                FallbackValue = string.Empty
                            },
                            ElementStyle = isDecimal ? _rightCellStyle : _leftCellStyle
                        });
                    }
                }
            }

            Folders.Clear();
            Model.Folders.Sort();
            foreach (FolderModel folderModel in Model.Folders)
            {
                var folderViewModel = new FolderViewModel(this, folderModel);
                Folders.Add(folderViewModel);
            }

        }

        internal bool DeleteFolder(FolderViewModel symbol)
        {
            return Folders.Remove(symbol);
        }


        internal bool DeleteSymbol(SymbolViewModel symbol)
        {
            return Symbols.Remove(symbol);
        }

        internal async Task StartTaskAsync()
        {
            DataToModel();
            MarketModel model = Model;
            _cancel = new CancellationTokenSource();
            while (!_cancel.Token.IsCancellationRequested && model.Active)
            {
                Log.Trace($"{model.Provider} download {model.Resolution} {model.FromDate:d}");
                var logger = new HostDomainLogger();
                try
                {
                    _factory = new Isolated<ProviderFactory>();
                    _cancel = new CancellationTokenSource();
                    await Task.Run(() => model = _factory.Value.Run(model, _settingsModel, logger), _cancel.Token);
                    _factory.Dispose();
                    _factory = null;
                }
                catch (AppDomainUnloadedException)
                {
                    Log.Trace($"Market {model.Name} canceled by user");
                    _factory = null;
                    model.Active = false;
                }
                catch (Exception ex)
                {
                    Log.Trace($"{ex.GetType()}: {ex.Message}");
                    _factory.Dispose();
                    _factory = null;
                    model.Active = false;
                }

                if (logger.IsError)
                {
                    Log.Trace($"{Model.Provider} download failed");
                }

                // Update view
                Model = null;
                Model = model;
                DataFromModel();
            }

            _cancel = null;
            if (model.Active)
            {
                Log.Trace($"{Model.Provider} download complete");
            }
        }

        private void StopTask()
        {
            if (_cancel != null)
            {
                _cancel.Cancel();
            }

            if (_factory != null)
            {
                _factory.Dispose();
            }
        }

        private async void OnActiveCommand(bool value)
        {
            if (value)
            {
                await StartTaskAsync();
            }
            else
            {
                StopTask();
            }
        }

        private async void OnStartCommand()
        {
            Active = true;
            await StartTaskAsync();
        }

        private void OnStopCommand()
        {
            StopTask();
            Active = false;
        }

        private void AddSymbol()
        {
            var symbol = new SymbolViewModel(this, new SymbolModel());
            Symbols.Add(symbol);
            Folders.ToList().ForEach(m => m.Refresh());
        }

        private void AddAllSymbols()
        {
            _parent.IsBusy = true;
            IEnumerable<SymbolModel> symbols = ProviderFactory.GetAllSymbols(Model);
            foreach (SymbolModel symbol in symbols)
            {
                SymbolModel sym = Model.Symbols.Find(m => m.Name.Equals(symbol.Name));
                if (sym != null)
                {
                    sym.Properties = symbol.Properties;
                }
                else
                {
                    Model.Symbols.Add(symbol);
                }
            }

            Folders.ToList().ForEach(m => m.Refresh());
            DataFromModel();
            _parent.IsBusy = false;
        }

        private void DeleteSymbols(IList symbols)
        {
            Debug.Assert(symbols != null);
            if (Symbols.Count == 0 || symbols.Count == 0)
                return;

            // Create a copy of the list before remove
            List<SymbolViewModel> list = symbols.Cast<SymbolViewModel>()?.ToList();
            Debug.Assert(list != null);

            int pos = Symbols.IndexOf(list.First());
            foreach (SymbolViewModel symbol in list)
            {
                Symbols.Remove(symbol);
            }

            DataToModel();
            if (Symbols.Count > 0)
            {
                SelectedSymbol = Symbols[Math.Min(pos, Symbols.Count - 1)];
            }
        }

        private void ImportSymbols()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Multiselect = false;
            openFileDialog.Filter = "symbol file (*.csv)|*.csv|All files (*.*)|*.*";
            if (openFileDialog.ShowDialog() == false)
                return;

            try
            {
                foreach (string fileName in openFileDialog.FileNames)
                {
                    using (StreamReader r = new StreamReader(fileName))
                    {
                        while (!r.EndOfStream)
                        {
                            string name = r.ReadLine();
                            if (!Model.Symbols.Exists(m => m.Name.Equals(name)))
                            {
                                var symbol = new SymbolModel() { Name = name };
                                Model.Symbols.Add(symbol);
                            }
                        }
                    }
                }

                Folders.ToList().ForEach(m => m.Refresh());
                DataFromModel();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, ex.GetType().ToString());
            }
        }

        private void ExportSymbols(IList symbols)
        {
            Debug.Assert(symbols != null);
            if (symbols.Count == 0)
                return;

            DataToModel();
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "symbol file (*.csv)|*.csv|All files (*.*)|*.*";
            if (saveFileDialog.ShowDialog() == false)
                return;

            try
            {
                string fileName = saveFileDialog.FileName;
                using (StreamWriter file = File.CreateText(fileName))
                {
                    foreach (SymbolViewModel symbol in symbols)
                    {
                        file.WriteLine(symbol.Model.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, ex.GetType().ToString());
            }
        }
    }
}
