"""Send a test audio file to the running voice agent via WebSocket."""
import asyncio
import json
import sys

try:
    import websockets
except ImportError:
    print("Installing websockets...")
    import subprocess
    subprocess.check_call([sys.executable, "-m", "pip", "install", "websockets", "-q"])
    import websockets


async def test():
    wav_path = 'D:/elbruno/nvidia-voiceagent-cs/tests/NvidiaVoiceAgent.Core.Tests/TestData/hey_can_you_help_me.wav'
    with open(wav_path, 'rb') as f:
        wav_data = f.read()
    print(f'Audio: {len(wav_data)} bytes')

    uri = 'ws://localhost:5003/ws/voice'
    async with websockets.connect(uri) as ws:
        await ws.send(wav_data)
        print('Sent audio, waiting for response...')

        try:
            while True:
                msg = await asyncio.wait_for(ws.recv(), timeout=60)
                if isinstance(msg, str):
                    data = json.loads(msg)
                    rtype = data.get('type', 'unknown')
                    print(f'Response type: {rtype}')
                    if 'transcript' in data:
                        print(f'  TRANSCRIPT: "{data["transcript"]}"')
                    if 'response' in data:
                        resp = data['response'][:200]
                        print(f'  LLM Response: "{resp}"')
                    if rtype == 'voice':
                        break
                else:
                    print(f'Binary: {len(msg)} bytes')
        except asyncio.TimeoutError:
            print('Timeout waiting for response')

asyncio.run(test())
