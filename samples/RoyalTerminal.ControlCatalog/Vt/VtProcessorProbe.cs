// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.ControlCatalog;

internal sealed class VtProcessorProbe : IDisposable
{
    private readonly object _sync = new();
    private readonly Queue<string> _responses = [];
    private string? _lastTitle;
    private int _bellCount;

    private VtProcessorProbe(string name, TerminalScreen screen, IVtProcessor processor)
    {
        Name = name;
        Screen = screen;
        Processor = processor;

        processor.ResponseCallback = data =>
        {
            string response = Encoding.ASCII.GetString(data);
            lock (_sync)
            {
                _responses.Enqueue(response);
            }
        };

        processor.TitleCallback = title =>
        {
            lock (_sync)
            {
                _lastTitle = title;
            }
        };

        processor.BellCallback = () =>
        {
            lock (_sync)
            {
                _bellCount++;
            }
        };
    }

    public string Name { get; }

    public TerminalScreen Screen { get; }

    public IVtProcessor Processor { get; }

    public string? LastTitle
    {
        get
        {
            lock (_sync)
            {
                return _lastTitle;
            }
        }
    }

    public int BellCount
    {
        get
        {
            lock (_sync)
            {
                return _bellCount;
            }
        }
    }

    public static VtProcessorProbe CreateManaged(int columns, int rows)
    {
        TerminalScreen screen = new(columns, rows, 2_000);
        IVtProcessor processor = new BasicVtProcessor(screen);
        return new VtProcessorProbe(CatalogConstants.ManagedProbeName, screen, processor);
    }

    public static bool TryCreateGhostty(int columns, int rows, out VtProcessorProbe probe, out string? reason)
    {
        try
        {
            TerminalScreen screen = new(columns, rows, 2_000);
            IVtProcessor processor = new GhosttyVtProcessor(screen);
            probe = new VtProcessorProbe(CatalogConstants.GhosttyProbeName, screen, processor);
            reason = null;
            return true;
        }
        catch (Exception ex)
        {
            probe = null!;
            reason = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    public void NotifyResize(int columns, int rows, int widthPx, int heightPx)
    {
        Processor.NotifyResize(columns, rows, widthPx, heightPx);
    }

    public void Send(string sequence)
    {
        byte[] payload = Encoding.UTF8.GetBytes(sequence);
        Processor.Process(payload);
    }

    public bool TryTakeResponse(out string response)
    {
        lock (_sync)
        {
            if (_responses.Count == 0)
            {
                response = string.Empty;
                return false;
            }

            response = _responses.Dequeue();
            return true;
        }
    }

    public void ClearResponses()
    {
        lock (_sync)
        {
            _responses.Clear();
        }
    }

    public void ResetSignals()
    {
        lock (_sync)
        {
            _responses.Clear();
            _lastTitle = null;
            _bellCount = 0;
        }
    }

    public void Dispose()
    {
        Processor.Dispose();
    }
}
