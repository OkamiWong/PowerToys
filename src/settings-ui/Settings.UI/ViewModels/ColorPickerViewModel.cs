﻿// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Timers;
using global::PowerToys.GPOWrapper;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.PowerToys.Settings.UI.Library.Enumerations;
using Microsoft.PowerToys.Settings.UI.Library.Helpers;
using Microsoft.PowerToys.Settings.UI.Library.Interfaces;

namespace Microsoft.PowerToys.Settings.UI.ViewModels
{
    public class ColorPickerViewModel : Observable, IDisposable
    {
        private bool disposedValue;

        // Delay saving of settings in order to avoid calling save multiple times and hitting file in use exception. If there is no other request to save settings in given interval, we proceed to save it, otherwise we schedule saving it after this interval
        private const int SaveSettingsDelayInMs = 500;

        private GeneralSettings GeneralSettingsConfig { get; set; }

        private readonly ISettingsUtils _settingsUtils;
        private readonly object _delayedActionLock = new object();

        private readonly ColorPickerSettings _colorPickerSettings;
        private Timer _delayedTimer;

        private GpoRuleConfigured _enabledGpoRuleConfiguration;
        private bool _enabledStateIsGPOConfigured;
        private bool _isEnabled;

        private Func<string, int> SendConfigMSG { get; }

        public ColorPickerViewModel(
            ISettingsUtils settingsUtils,
            ISettingsRepository<GeneralSettings> settingsRepository,
            ISettingsRepository<ColorPickerSettings> colorPickerSettingsRepository,
            Func<string, int> ipcMSGCallBackFunc)
        {
            // Obtain the general PowerToy settings configurations
            if (settingsRepository == null)
            {
                throw new ArgumentNullException(nameof(settingsRepository));
            }

            SelectableColorRepresentations = new Dictionary<ColorRepresentationType, string>
            {
                { ColorRepresentationType.CMYK, "CMYK - cmyk(100%, 50%, 75%, 0%)" },
                { ColorRepresentationType.HEX,  "HEX - ffaa00" },
                { ColorRepresentationType.HSB,  "HSB - hsb(100, 50%, 75%)" },
                { ColorRepresentationType.HSI,  "HSI - hsi(100, 50%, 75%)" },
                { ColorRepresentationType.HSL,  "HSL - hsl(100, 50%, 75%)" },
                { ColorRepresentationType.HSV,  "HSV - hsv(100, 50%, 75%)" },
                { ColorRepresentationType.HWB,  "HWB - hwb(100, 50%, 75%)" },
                { ColorRepresentationType.NCol, "NCol - R10, 50%, 75%" },
                { ColorRepresentationType.RGB,  "RGB - rgb(100, 50, 75)" },
                { ColorRepresentationType.CIELAB, "CIE LAB - CIELab(76, 21, 80)" },
                { ColorRepresentationType.CIEXYZ, "CIE XYZ - xyz(56, 50, 7)" },
                { ColorRepresentationType.VEC4, "VEC4 - (1.0f, 0.7f, 0f, 1f)" },
                { ColorRepresentationType.DecimalValue, "Decimal - 16755200" },
                { ColorRepresentationType.HexInteger, "HEX Integer - 0xFFAA00EE" },
            };

            GeneralSettingsConfig = settingsRepository.SettingsConfig;

            _settingsUtils = settingsUtils ?? throw new ArgumentNullException(nameof(settingsUtils));

            if (colorPickerSettingsRepository == null)
            {
                throw new ArgumentNullException(nameof(colorPickerSettingsRepository));
            }

            _colorPickerSettings = colorPickerSettingsRepository.SettingsConfig;
            _enabledGpoRuleConfiguration = GPOWrapper.GetConfiguredColorPickerEnabledValue();
            if (_enabledGpoRuleConfiguration == GpoRuleConfigured.Disabled || _enabledGpoRuleConfiguration == GpoRuleConfigured.Enabled)
            {
                // Get the enabled state from GPO.
                _enabledStateIsGPOConfigured = true;
                _isEnabled = _enabledGpoRuleConfiguration == GpoRuleConfigured.Enabled;
            }
            else
            {
                _isEnabled = GeneralSettingsConfig.Enabled.ColorPicker;
            }

            // set the callback functions value to hangle outgoing IPC message.
            SendConfigMSG = ipcMSGCallBackFunc;

            _delayedTimer = new Timer();
            _delayedTimer.Interval = SaveSettingsDelayInMs;
            _delayedTimer.Elapsed += DelayedTimer_Tick;
            _delayedTimer.AutoReset = false;

            InitializeColorFormats();
        }

        /// <summary>
        /// Gets a list with all selectable <see cref="ColorRepresentationType"/>s
        /// </summary>
        public IReadOnlyDictionary<ColorRepresentationType, string> SelectableColorRepresentations { get; }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_enabledStateIsGPOConfigured)
                {
                    // If it's GPO configured, shouldn't be able to change this state.
                    return;
                }

                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged(nameof(IsEnabled));

                    // Set the status of ColorPicker in the general settings
                    GeneralSettingsConfig.Enabled.ColorPicker = value;
                    var outgoing = new OutGoingGeneralSettings(GeneralSettingsConfig);

                    SendConfigMSG(outgoing.ToString());
                }
            }
        }

        public bool IsEnabledGpoConfigured
        {
            get => _enabledStateIsGPOConfigured;
        }

        public bool ChangeCursor
        {
            get => _colorPickerSettings.Properties.ChangeCursor;
            set
            {
                if (_colorPickerSettings.Properties.ChangeCursor != value)
                {
                    _colorPickerSettings.Properties.ChangeCursor = value;
                    OnPropertyChanged(nameof(ChangeCursor));
                    NotifySettingsChanged();
                }
            }
        }

        public HotkeySettings ActivationShortcut
        {
            get => _colorPickerSettings.Properties.ActivationShortcut;
            set
            {
                if (_colorPickerSettings.Properties.ActivationShortcut != value)
                {
                    _colorPickerSettings.Properties.ActivationShortcut = value;
                    OnPropertyChanged(nameof(ActivationShortcut));
                    NotifySettingsChanged();
                }
            }
        }

        public ColorRepresentationType SelectedColorRepresentationValue
        {
            get => _colorPickerSettings.Properties.CopiedColorRepresentation;
            set
            {
                if (_colorPickerSettings.Properties.CopiedColorRepresentation != value)
                {
                    _colorPickerSettings.Properties.CopiedColorRepresentation = value;
                    OnPropertyChanged(nameof(SelectedColorRepresentationValue));
                    NotifySettingsChanged();
                }
            }
        }

        public int ActivationBehavior
        {
            get
            {
                return (int)_colorPickerSettings.Properties.ActivationAction;
            }

            set
            {
                if (value != (int)_colorPickerSettings.Properties.ActivationAction)
                {
                    _colorPickerSettings.Properties.ActivationAction = (ColorPickerActivationAction)value;
                    OnPropertyChanged(nameof(ActivationBehavior));
                    NotifySettingsChanged();
                }
            }
        }

        public bool ShowColorName
        {
            get => _colorPickerSettings.Properties.ShowColorName;
            set
            {
                if (_colorPickerSettings.Properties.ShowColorName != value)
                {
                    _colorPickerSettings.Properties.ShowColorName = value;
                    OnPropertyChanged(nameof(ShowColorName));
                    NotifySettingsChanged();
                }
            }
        }

        public ObservableCollection<ColorFormatModel> ColorFormats { get; } = new ObservableCollection<ColorFormatModel>();

        private void InitializeColorFormats()
        {
            var visibleFormats = _colorPickerSettings.Properties.VisibleColorFormats;
            var formatsUnordered = new List<ColorFormatModel>();

            var hexFormatName = ColorRepresentationType.HEX.ToString();
            var rgbFormatName = ColorRepresentationType.RGB.ToString();
            var hslFormatName = ColorRepresentationType.HSL.ToString();
            var hsvFormatName = ColorRepresentationType.HSV.ToString();
            var cmykFormatName = ColorRepresentationType.CMYK.ToString();
            var hsbFormatName = ColorRepresentationType.HSB.ToString();
            var hsiFormatName = ColorRepresentationType.HSI.ToString();
            var hwbFormatName = ColorRepresentationType.HWB.ToString();
            var ncolFormatName = ColorRepresentationType.NCol.ToString();
            var cielabFormatName = ColorRepresentationType.CIELAB.ToString();
            var ciexyzFormatName = ColorRepresentationType.CIEXYZ.ToString();
            var vec4FormatName = ColorRepresentationType.VEC4.ToString();
            var hexIntegerFormatName = "HEX Int";
            var decimalFormatName = "Decimal";

            formatsUnordered.Add(new ColorFormatModel(hexFormatName, "ef68ff", visibleFormats.ContainsKey(hexFormatName) && visibleFormats[hexFormatName]));
            formatsUnordered.Add(new ColorFormatModel(rgbFormatName, "rgb(239, 104, 255)", visibleFormats.ContainsKey(rgbFormatName) && visibleFormats[rgbFormatName]));
            formatsUnordered.Add(new ColorFormatModel(hslFormatName, "hsl(294, 100%, 70%)", visibleFormats.ContainsKey(hslFormatName) && visibleFormats[hslFormatName]));
            formatsUnordered.Add(new ColorFormatModel(hsvFormatName, "hsv(294, 59%, 100%)", visibleFormats.ContainsKey(hsvFormatName) && visibleFormats[hsvFormatName]));
            formatsUnordered.Add(new ColorFormatModel(cmykFormatName, "cmyk(6%, 59%, 0%, 0%)", visibleFormats.ContainsKey(cmykFormatName) && visibleFormats[cmykFormatName]));
            formatsUnordered.Add(new ColorFormatModel(hsbFormatName, "hsb(100, 50%, 75%)", visibleFormats.ContainsKey(hsbFormatName) && visibleFormats[hsbFormatName]));
            formatsUnordered.Add(new ColorFormatModel(hsiFormatName, "hsi(100, 50%, 75%)", visibleFormats.ContainsKey(hsiFormatName) && visibleFormats[hsiFormatName]));
            formatsUnordered.Add(new ColorFormatModel(hwbFormatName, "hwb(100, 50%, 75%)", visibleFormats.ContainsKey(hwbFormatName) && visibleFormats[hwbFormatName]));
            formatsUnordered.Add(new ColorFormatModel(ncolFormatName, "R10, 50%, 75%", visibleFormats.ContainsKey(ncolFormatName) && visibleFormats[ncolFormatName]));
            formatsUnordered.Add(new ColorFormatModel(cielabFormatName, "CIELab(66, 72, -52)", visibleFormats.ContainsKey(cielabFormatName) && visibleFormats[cielabFormatName]));
            formatsUnordered.Add(new ColorFormatModel(ciexyzFormatName, "XYZ(59, 35, 98)", visibleFormats.ContainsKey(ciexyzFormatName) && visibleFormats[ciexyzFormatName]));
            formatsUnordered.Add(new ColorFormatModel(vec4FormatName, "(0.94f, 0.41f, 1.00f, 1f)", visibleFormats.ContainsKey(vec4FormatName) && visibleFormats[vec4FormatName]));
            formatsUnordered.Add(new ColorFormatModel(decimalFormatName, "15689983", visibleFormats.ContainsKey(decimalFormatName) && visibleFormats[decimalFormatName]));
            formatsUnordered.Add(new ColorFormatModel(hexIntegerFormatName, "0xFFAA00EE", visibleFormats.ContainsKey(hexIntegerFormatName) && visibleFormats[hexIntegerFormatName]));

            foreach (var storedColorFormat in _colorPickerSettings.Properties.VisibleColorFormats)
            {
                var predefinedFormat = formatsUnordered.FirstOrDefault(it => it.Name == storedColorFormat.Key);
                if (predefinedFormat != null)
                {
                    predefinedFormat.PropertyChanged += ColorFormat_PropertyChanged;
                    ColorFormats.Add(predefinedFormat);
                    formatsUnordered.Remove(predefinedFormat);
                }
            }

            // settings file might not have all formats listed, add remaining ones we support
            foreach (var remainingColorFormat in formatsUnordered)
            {
                remainingColorFormat.PropertyChanged += ColorFormat_PropertyChanged;
                ColorFormats.Add(remainingColorFormat);
            }

            // Reordering colors with buttons: disable first and last buttons
            ColorFormats[0].CanMoveUp = false;
            ColorFormats[ColorFormats.Count - 1].CanMoveDown = false;

            ColorFormats.CollectionChanged += ColorFormats_CollectionChanged;
        }

        private void ColorFormats_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // Reordering colors with buttons: update buttons availability depending on order
            if (ColorFormats.Count > 0)
            {
                foreach (var color in ColorFormats)
                {
                    color.CanMoveUp = true;
                    color.CanMoveDown = true;
                }

                ColorFormats[0].CanMoveUp = false;
                ColorFormats[ColorFormats.Count - 1].CanMoveDown = false;
            }

            UpdateColorFormats();
            ScheduleSavingOfSettings();
        }

        private void ColorFormat_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            UpdateColorFormats();
            ScheduleSavingOfSettings();
        }

        private void ScheduleSavingOfSettings()
        {
            lock (_delayedActionLock)
            {
                if (_delayedTimer.Enabled)
                {
                    _delayedTimer.Stop();
                }

                _delayedTimer.Start();
            }
        }

        private void DelayedTimer_Tick(object sender, EventArgs e)
        {
            lock (_delayedActionLock)
            {
                _delayedTimer.Stop();
                NotifySettingsChanged();
            }
        }

        private void UpdateColorFormats()
        {
            _colorPickerSettings.Properties.VisibleColorFormats.Clear();
            foreach (var colorFormat in ColorFormats)
            {
                _colorPickerSettings.Properties.VisibleColorFormats.Add(colorFormat.Name, colorFormat.IsShown);
            }
        }

        private void NotifySettingsChanged()
        {
            // Using InvariantCulture as this is an IPC message
            SendConfigMSG(
                   string.Format(
                       CultureInfo.InvariantCulture,
                       "{{ \"powertoys\": {{ \"{0}\": {1} }} }}",
                       ColorPickerSettings.ModuleName,
                       JsonSerializer.Serialize(_colorPickerSettings)));
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _delayedTimer.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
