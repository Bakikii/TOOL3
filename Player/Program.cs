using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Windows;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using CommandLine;
using CommandLine.Text;
using Core.Audio;
using T3.Core;
using T3.Core.Animation;
using T3.Core.IO;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using Color = SharpDX.Color;
using Device = SharpDX.Direct3D11.Device;

namespace T3
{
    public class Program
    {
        public static Device Device { get; private set; }

        public class Options
        {
            [Option(Default = false, Required = false, HelpText = "Disable vsync")]
            public bool NoVsync { get; set; }

            [Option(Default = 1920, Required = false, HelpText = "Defines the width")]
            public int Width { get; set; }

            [Option(Default = 1080, Required = false, HelpText = "Defines the height")]
            public int Height { get; set; }

            public Size Size => new Size(Width, Height);

            [Option(Default = false, Required = false, HelpText = "Run in windowed mode")]
            public bool Windowed { get; set; }

            [Option(Default = false, Required = false, HelpText = "Loops the demo")]
            public bool Loop { get; set; }

            [Option(Default = true, Required = false, HelpText = "Show log messages.")]
            public bool Logging { get; set; }
        }

        private static Options ParseCommandLine(string[] args)
        {
            Options parsedOptions = null;
            var parser = new Parser(config =>
                                    {
                                        config.HelpWriter = null;
                                        config.AutoVersion = false;
                                    });
            var parserResult = parser.ParseArguments<Options>(args);
            var helpText = HelpText.AutoBuild(parserResult,
                                              h =>
                                              {
                                                  h.AdditionalNewLineAfterOption = false;
                                                  h.Heading = "still::partial - v0.1";
                                                  h.Copyright = "Copyright (c) 2021 lucid & pixtur";
                                                  h.AutoVersion = false;
                                                  return h;
                                              },
                                              e => e);

            parserResult.WithParsed(o => { parsedOptions = o; })
                        .WithNotParsed(o => { Console.WriteLine(helpText); });
            return parsedOptions;
        }

        [STAThread]
        private static void Main(string[] args)
        {
            var tmp = new ProjectSettings(saveOnQuit: false);            
            
            Options options = ParseCommandLine(args);
            if (options == null)
                return;

            _vsync = !options.NoVsync;
            Console.WriteLine($"using vsync: {_vsync}, windowed: {options.Windowed}, size: {options.Size}, loop: {options.Loop}, logging: {options.Logging}");
            var form = new RenderForm("still::partial")
                           {
                               ClientSize = options.Size,
                               AllowUserResizing = false,
                               Icon = new Icon(@"Resources\t3\t3.ico")
                           };
            
            // SwapChain description
            var desc = new SwapChainDescription()
                           {
                               BufferCount = 3,
                               ModeDescription = new ModeDescription(form.ClientSize.Width, form.ClientSize.Height,
                                                                     new Rational(60, 1), Format.R8G8B8A8_UNorm),
                               IsWindowed = options.Windowed,
                               OutputHandle = form.Handle,
                               SampleDescription = new SampleDescription(1, 0),
                               SwapEffect = SwapEffect.FlipDiscard,
                               Flags = SwapChainFlags.AllowModeSwitch,
                               Usage = Usage.RenderTargetOutput
                           };

            // Create Device and SwapChain
            #if DEBUG || FORCE_D3D_DEBUG
            var deviceCreationFlags = DeviceCreationFlags.Debug;
            #else
            var deviceCreationFlags = DeviceCreationFlags.None;
            #endif
            Device.CreateWithSwapChain(DriverType.Hardware, deviceCreationFlags, desc, out var device, out _swapChain);
            var context = device.ImmediateContext;
            Device = device;

            if (_swapChain.IsFullScreen)
            {
                Cursor.Hide();
            }

            // Ignore all windows events
            var factory = _swapChain.GetParent<Factory>();
            factory.MakeWindowAssociation(form.Handle, WindowAssociationFlags.IgnoreAll);

            bool startedWindowed = options.Windowed;

            form.KeyDown += HandleKeyDown;
            form.KeyUp += HandleKeyUp;

            form.KeyUp += (sender, keyArgs) =>
                          {
                              if (startedWindowed && keyArgs.Alt && keyArgs.KeyCode == Keys.Enter)
                              {
                                  _swapChain.IsFullScreen = !_swapChain.IsFullScreen;
                                  RebuildBackBuffer(form, device, ref _renderView, ref _backBuffer, ref _swapChain);
                                  if (_swapChain.IsFullScreen)
                                  {
                                      Cursor.Hide();
                                  }
                                  else
                                  {
                                      Cursor.Show();
                                  }
                              }

                              if (keyArgs.KeyCode == Keys.Escape)
                              {
                                  Application.Exit();
                              }
                          };

            // New RenderTargetView from the backbuffer
            _backBuffer = Texture2D.FromSwapChain<Texture2D>(_swapChain, 0);
            _renderView = new RenderTargetView(device, _backBuffer);

            ResourceManager.Init(device);
            ResourceManager resourceManager = ResourceManager.Instance();
            FullScreenVertexShaderId =
                resourceManager.CreateVertexShaderFromFile(@"Resources\lib\dx11\fullscreen-texture.hlsl", "vsMain", "vs-fullscreen-texture", () => { });
            FullScreenPixelShaderId =
                resourceManager.CreatePixelShaderFromFile(@"Resources\lib\dx11\fullscreen-texture.hlsl", "psMain", "ps-fullscreen-texture", () => { });

            Assembly operatorsAssembly;
            try
            {
                operatorsAssembly = Assembly.LoadFrom("Operators.dll");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error loading operator assembly: '{e.Message}'");
                return;
            }

            _model = new Model(operatorsAssembly, options.Logging);
            _model.Load();

            var symbols = SymbolRegistry.Entries;
            var demoSymbol = symbols.First(entry => entry.Value.Name == ProjectSettings.Config.MainOperatorName).Value;

            _playback = new Playback();
            
            // create instance of project op, all children are create automatically
            _project = demoSymbol.CreateInstance(Guid.NewGuid());
            _evalContext = new EvaluationContext();

            //var soundTrackPath = @"Resources\proj-partial\soundtrack\partial.mp3";
            _soundtrack = demoSymbol.AudioClips.SingleOrDefault(ac => ac.IsSoundtrack);
            
            var usingSoundtrack = _soundtrack != null && File.Exists(_soundtrack.FilePath);
          
            if (usingSoundtrack)
            {
                _playback.Bpm = _soundtrack.Bpm;
                AudioEngine.UseAudioClip(_soundtrack, 0);
                AudioEngine.CompleteFrame(_playback);
            }
            else
            {
                _playback.PlaybackSpeed = 0.5f; // Todo: Clarify, if this is a work around for default BPM mismatch 
            }

            // sample some frames to preload all shaders and resources
            if (usingSoundtrack)
            {
                var songDurationInSecs = Playback.GetSongDurationInSecs();
                for (double timeInSecs = 0; timeInSecs < songDurationInSecs; timeInSecs += 1.0)
                {
                    Log.Info($"Pre-evaluate at: {timeInSecs}s");
                    Playback.Current.TimeInSecs = timeInSecs;
                    Playback.Current.TimeInBars = timeInSecs / 120.0 * 240.0;

                    DirtyFlag.IncrementGlobalTicks();
                    DirtyFlag.InvalidationRefFrame++;

                    context.Rasterizer.SetViewport(new Viewport(0, 0, form.ClientSize.Width, form.ClientSize.Height, 0.0f, 1.0f));
                    context.OutputMerger.SetTargets(_renderView);

                    _evalContext.Reset();
                    _evalContext.RequestedResolution = new Size2(options.Width, options.Height);

                    if (_project.Outputs[0] is Slot<Texture2D> textureOutput)
                    {
                        textureOutput.Invalidate();
                        textureOutput.GetValue(_evalContext);
                    }
                }
            }

            // start playback           
            _playback.Update();
            _playback.TimeInBars = 0;
            _playback.PlaybackSpeed = 1.0;

            var stopwatch = new Stopwatch();
            stopwatch.Start();
            Int64 lastElapsedTicks = stopwatch.ElapsedTicks;

            var rasterizerDesc = new RasterizerStateDescription()
                                     {
                                         FillMode = FillMode.Solid,
                                         CullMode = CullMode.None,
                                         IsScissorEnabled = false,
                                         IsDepthClipEnabled = false
                                     };
            var rasterizerState = new RasterizerState(device, rasterizerDesc);

            // Main loop
            RenderLoop.Run(form, () =>
                                 {
                                     _playback.Update();

                                     Log.Debug($" render at playback time {_playback.TimeInSecs:0.00}s");
                                     if (_soundtrack != null)
                                     {
                                         AudioEngine.UseAudioClip(_soundtrack, _playback.TimeInSecs);
                                         AudioEngine.CompleteFrame(_playback);
                                         if (_playback.TimeInSecs >= _soundtrack.LengthInSeconds + _soundtrack.StartTime)
                                         {
                                             if (options.Loop)
                                             {
                                                 _playback.TimeInSecs = 0.0;
                                             }
                                             else
                                             {
                                                 Application.Exit();
                                             }
                                         }
                                     }

                                     DirtyFlag.IncrementGlobalTicks();
                                     DirtyFlag.InvalidationRefFrame++;

                                     context.Rasterizer.SetViewport(new Viewport(0, 0, form.ClientSize.Width, form.ClientSize.Height, 0.0f, 1.0f));
                                     context.OutputMerger.SetTargets(_renderView);

                                     _evalContext.Reset();
                                     _evalContext.RequestedResolution = new Size2(options.Width, options.Height);

                                     if (_project.Outputs[0] is Slot<Texture2D> textureOutput)
                                     {
                                         textureOutput.Invalidate();
                                         Texture2D tex = textureOutput.GetValue(_evalContext);
                                         if (tex != null)
                                         {
                                             context.Rasterizer.State = rasterizerState;
                                             if (resourceManager.Resources[FullScreenVertexShaderId] is VertexShaderResource vsr)
                                                 context.VertexShader.Set(vsr.VertexShader);
                                             if (resourceManager.Resources[FullScreenPixelShaderId] is PixelShaderResource psr)
                                                 context.PixelShader.Set(psr.PixelShader);
                                             var srv = new ShaderResourceView(device, tex);
                                             context.PixelShader.SetShaderResource(0, srv);

                                             context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
                                             context.ClearRenderTargetView(_renderView, new Color(0.45f, 0.55f, 0.6f, 1.0f));
                                             context.Draw(3, 0);
                                             context.PixelShader.SetShaderResource(0, null);
                                         }
                                     }

                                     _swapChain.Present(_vsync ? 1 : 0, PresentFlags.None);
                                 });

            // Release all resources
            _renderView.Dispose();
            _backBuffer.Dispose();
            context.ClearState();
            context.Flush();
            device.Dispose();
            context.Dispose();
            _swapChain.Dispose();
            factory.Dispose();
        }

        private static void HandleKeyDown(object sender, KeyEventArgs e)
        {
            var keyIndex = (int)e.KeyCode;
            if (keyIndex >= Core.IO.KeyHandler.PressedKeys.Length)
            {
                Log.Warning($"Ignoring out of range key code {e.KeyCode} with index {keyIndex}");
            }
            else
            {
                Core.IO.KeyHandler.PressedKeys[keyIndex] = true;
            }
        }

        private static void HandleKeyUp(object sender, KeyEventArgs e)
        {
            var keyIndex = (int)e.KeyCode;
            if (keyIndex < Core.IO.KeyHandler.PressedKeys.Length)
            {
                Core.IO.KeyHandler.PressedKeys[keyIndex] = false;
            }
        }

        private static void RebuildBackBuffer(RenderForm form, Device device, ref RenderTargetView rtv, ref Texture2D buffer, ref SwapChain swapChain)
        {
            rtv.Dispose();
            buffer.Dispose();
            swapChain.ResizeBuffers(3, form.ClientSize.Width, form.ClientSize.Height, Format.Unknown, SwapChainFlags.AllowModeSwitch);
            buffer = Texture2D.FromSwapChain<Texture2D>(swapChain, 0);
            rtv = new RenderTargetView(device, buffer);
        }

        // private static bool _inResize;
        private static bool _vsync;
        private static SwapChain _swapChain;
        private static RenderTargetView _renderView;
        private static Texture2D _backBuffer;
        private static Model _model;
        private static Instance _project;
        private static EvaluationContext _evalContext;
        private static Playback _playback;
        private static AudioClip _soundtrack;
        private static uint FullScreenVertexShaderId { get; set; }
        private static uint FullScreenPixelShaderId { get; set; }
    }
}