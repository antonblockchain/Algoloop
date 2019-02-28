﻿/*
 * Copyright 2019 Capnode AB
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

using Algoloop.Model;
using QuantConnect;
using QuantConnect.Configuration;
using QuantConnect.ToolBox.DukascopyDownloader;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Algoloop.Provider
{
    class Dukascopy : IProvider
    {
        private readonly IEnumerable<string> _majors = new[] { "AUDUSD", "EURUSD", "GBPUSD", "NZDUSD", "USDCAD", "USDCHF", "USDJPY" };
        private readonly IEnumerable<string> _crosses = new[]
        {
            "AUDCAD", "AUDCHF", "AUDJPY", "AUDNZD", "AUDSGD", "CADCHF", "CADHKD", "CADJPY", "CHFJPY", "CHFPLN", "CHFSGD",
            "EURAUD", "EURCAD", "EURCHF", "EURDKK", "EURGBP", "EURHKD", "EURHUF", "EURJPY", "EURMXN", "EURNOK", "EURNZD",
            "EURPLN", "EURRUB", "EURSEK", "EURSGD", "EURTRY", "EURZAR", "GBPAUD", "GBPCAD", "GBPCHF", "GBPJPY", "GBPNZD",
            "HKDJPY", "MXNJPY", "NZDCAD", "NZDCHF", "NZDJPY", "NZDSGD", "XPDUSD", "XPTUSD", "SGDJPY", "USDBRL", "USDCNY",
            "USDDKK", "USDHKD", "USDHUF", "USDMXN", "USDNOK", "USDPLN", "USDRUB", "USDSEK", "USDSGD", "USDTRY", "USDZAR", "ZARJPY"
        };
        private readonly IEnumerable<string> _metals = new[] { "XAGUSD", "XAUUSD", "WTICOUSD", "NATGASUSD" };
        private readonly IEnumerable<string> _indices = new[]
        {
            "AU200AUD", "CH20CHF", "DE30EUR", "ES35EUR", "EU50EUR", "FR40EUR", "UK100GBP", "HK33HKD", "IT40EUR", "JP225JPY",
            "NL25EUR", "US30USD", "SPX500USD", "NAS100USD"
        };

        public void Download(MarketModel model, SettingsModel settings, IList<string> symbols)
        {
            Config.Set("map-file-provider", "QuantConnect.Data.Auxiliary.LocalDiskMapFileProvider");
            Config.Set("data-directory", settings.DataFolder);

            string resolution = model.Resolution.Equals(Resolution.Tick) ? "all" : model.Resolution.ToString();
            DateTime fromDate = model.FromDate.Date;
            if (fromDate < DateTime.Today)
            {
                DateTime nextDate = fromDate.AddDays(1);
                DukascopyDownloaderProgram.DukascopyDownloader(symbols, resolution, fromDate, nextDate.AddMilliseconds(-1));
                model.FromDate = nextDate;
            }
            model.Active = model.FromDate < DateTime.Today;
        }

        public IEnumerable<SymbolModel> GetAllSymbols(MarketModel market)
        {
            var list = new List<SymbolModel>();
            list.AddRange(_majors.Select(m => new SymbolModel() { Name = m, Properties = new Dictionary<string, string> { { "Category", "Majors" } } }));
            list.AddRange(_crosses.Select(m => new SymbolModel() { Name = m, Properties = new Dictionary<string, string> { { "Category", "Crosses" } } }));
            list.AddRange(_metals.Select(m => new SymbolModel() { Name = m, Properties = new Dictionary<string, string> { { "Category", "Metals" } } }));
            list.AddRange(_indices.Select(m => new SymbolModel() { Name = m, Properties = new Dictionary<string, string> { { "Category", "Indices" } } }));
            return list;
        }
    }
}
