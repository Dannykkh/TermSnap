# GPU 기반 터미널 렌더링 접근법

## 1. Warp 스타일 GPU 렌더링 구현 방법

### 필요한 기술 스택
- **그래픽 API**: SharpDX (DirectX 11/12) 또는 Veldrid (크로스플랫폼)
- **텍스트 렌더링**: FreeType 또는 HarfBuzz
- **UI 프레임워크**: WPF 컨트롤 내에 GPU 렌더링 임베딩 (D3DImage)

### 아키텍처 개요

```
┌─────────────────────────────────────────────┐
│ WPF Window                                  │
│  ┌───────────────────────────────────────┐  │
│  │ D3DImage (GPU 렌더링 표면)            │  │
│  │  ┌─────────────────────────────────┐  │  │
│  │  │ DirectX 11 Swap Chain          │  │  │
│  │  │   - Glyph Atlas Texture         │  │  │
│  │  │   - Instanced Quad Rendering    │  │  │
│  │  └─────────────────────────────────┘  │  │
│  └───────────────────────────────────────┘  │
└─────────────────────────────────────────────┘
```

### 구현 단계

#### Phase 1: Glyph Atlas 생성 (초기화 시 한 번)

```csharp
// 1. 모든 필요한 문자를 텍스처 아틀라스에 렌더링
class GlyphAtlas
{
    Texture2D atlasTexture;  // 2048x2048 또는 4096x4096
    Dictionary<char, GlyphRect> glyphPositions;

    void RenderGlyphsToAtlas()
    {
        // FreeType로 각 문자를 비트맵으로 렌더링
        // 텍스처 아틀라스에 팩킹 (Rect Packing Algorithm)
        // ASCII (256자) + 자주 쓰는 유니코드 (2000자) = 총 ~2256자
    }
}

struct GlyphRect
{
    float u0, v0, u1, v1;  // 텍스처 좌표
    float width, height;    // 크기
    float bearingX, bearingY; // 오프셋
}
```

#### Phase 2: Instanced Rendering (매 프레임)

```csharp
// 2. 터미널 버퍼를 Instanced Drawing으로 렌더링
class TerminalRenderer
{
    Buffer<GlyphInstance> instanceBuffer;  // GPU 버퍼

    void Render(TerminalBuffer buffer)
    {
        // CPU: 인스턴스 데이터 준비 (1ms 미만)
        var instances = new GlyphInstance[buffer.Rows * buffer.Columns];
        int idx = 0;
        for (int y = 0; y < buffer.Rows; y++)
        {
            for (int x = 0; x < buffer.Columns; x++)
            {
                var cell = buffer.GetCell(y, x);
                instances[idx++] = new GlyphInstance
                {
                    Position = new Vector2(x * cellWidth, y * cellHeight),
                    TexCoord = atlas.GetGlyph(cell.Character),
                    FgColor = cell.Foreground,
                    BgColor = cell.Background
                };
            }
        }

        // GPU로 업로드
        instanceBuffer.Update(instances);

        // GPU: 단일 Draw Call로 모든 문자 렌더링
        deviceContext.DrawInstanced(6, instances.Length, 0, 0);
        // 6 = quad의 vertex 수 (2 triangles)
        // instances.Length = 그릴 문자 개수
    }
}

struct GlyphInstance
{
    Vector2 Position;    // 화면 위치
    Vector4 TexCoord;    // Atlas 텍스처 좌표 (u0,v0,u1,v1)
    Color FgColor;       // 전경색
    Color BgColor;       // 배경색
}
```

#### Phase 3: Vertex Shader

```hlsl
// VertexShader.hlsl
struct VSInput
{
    uint vertexID : SV_VertexID;          // Quad의 정점 (0-5)
    float2 position : POSITION;            // Instance 위치
    float4 texCoord : TEXCOORD0;           // Atlas 좌표
    float4 fgColor : COLOR0;
    float4 bgColor : COLOR1;
};

struct PSInput
{
    float4 position : SV_Position;
    float2 texCoord : TEXCOORD0;
    float4 fgColor : COLOR0;
    float4 bgColor : COLOR1;
};

cbuffer Constants
{
    float2 screenSize;
    float2 cellSize;
};

PSInput main(VSInput input)
{
    PSInput output;

    // Quad의 4개 정점 생성 (0,0) (1,0) (0,1) (1,1)
    float2 quadPos = float2(
        vertexID == 1 || vertexID == 2 || vertexID == 4 ? 1.0 : 0.0,
        vertexID >= 2 ? 1.0 : 0.0
    );

    // 인스턴스 위치 + Quad 오프셋
    float2 worldPos = input.position + quadPos * cellSize;

    // 화면 좌표 변환 (-1 ~ 1)
    output.position = float4(
        worldPos.x / screenSize.x * 2.0 - 1.0,
        1.0 - worldPos.y / screenSize.y * 2.0,
        0.0,
        1.0
    );

    // 텍스처 좌표 보간
    output.texCoord = lerp(
        input.texCoord.xy,
        input.texCoord.zw,
        quadPos
    );

    output.fgColor = input.fgColor;
    output.bgColor = input.bgColor;

    return output;
}
```

#### Phase 4: Fragment Shader

```hlsl
// PixelShader.hlsl
Texture2D glyphAtlas : register(t0);
SamplerState linearSampler : register(s0);

struct PSInput
{
    float4 position : SV_Position;
    float2 texCoord : TEXCOORD0;
    float4 fgColor : COLOR0;
    float4 bgColor : COLOR1;
};

float4 main(PSInput input) : SV_Target
{
    // Atlas에서 Glyph 샘플링 (alpha 채널에 Glyph 모양)
    float alpha = glyphAtlas.Sample(linearSampler, input.texCoord).r;

    // 배경색 + 전경색 블렌딩
    float4 color = lerp(input.bgColor, input.fgColor, alpha);

    return color;
}
```

## 2. WPF와 통합 (D3DImage 사용)

```csharp
public class GpuTerminalControl : Image
{
    private D3DImage d3dImage;
    private DirectXRenderer renderer;

    public GpuTerminalControl()
    {
        // DirectX 11 초기화
        renderer = new DirectXRenderer();

        // D3DImage 생성 (WPF ↔ DirectX 브릿지)
        d3dImage = new D3DImage();
        d3dImage.IsFrontBufferAvailableChanged += OnIsFrontBufferAvailableChanged;

        Source = d3dImage;

        // 렌더링 루프 시작
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object sender, EventArgs e)
    {
        if (d3dImage.IsFrontBufferAvailable)
        {
            d3dImage.Lock();

            // DirectX로 렌더링
            renderer.Render(terminalBuffer);

            // WPF에 알림
            d3dImage.SetBackBuffer(D3DResourceType.IDirect3DSurface9,
                                   renderer.GetBackBuffer());
            d3dImage.AddDirtyRect(new Int32Rect(0, 0,
                                  d3dImage.PixelWidth,
                                  d3dImage.PixelHeight));

            d3dImage.Unlock();
        }
    }
}
```

## 3. 성능 비교

| 방법 | CPU 사용량 | GPU 사용량 | 프레임 시간 | 구현 난이도 |
|------|-----------|-----------|------------|------------|
| **현재 WPF (캐싱)** | 중간 | 낮음 | 5-10ms | 낮음 ✅ |
| **D3DImage + DirectX** | 낮음 | 중간 | 2-4ms | 높음 |
| **Warp (Rust + Metal)** | 매우 낮음 | 높음 | 1-2ms | 매우 높음 |

## 4. 구현 시 고려사항

### 장점
- **극한 성능**: 144Hz 모니터에서도 부드러운 60+ FPS
- **배터리 수명**: CPU 사용량 최소화
- **대량 출력**: 초당 수십만 줄도 처리 가능

### 단점
- **개발 비용**: 3-4주 이상의 개발 시간
- **유지보수**: 그래픽 버그, 드라이버 호환성 이슈
- **복잡도**: 셰이더, GPU 동기화, 메모리 관리
- **의존성**: SharpDX, FreeType 등 추가 라이브러리

## 5. 권장사항

### 현재 WPF 접근법 유지 (권장) ✅
**이유:**
1. **현재 성능도 충분**: 캐싱으로 5-10ms (100+ FPS 가능)
2. **개발 속도**: 기능 추가가 빠르고 안정적
3. **유지보수**: WPF 생태계의 혜택 (디버깅, 도구, 문서)
4. **실용성**: 대부분의 터미널 작업에 충분한 성능

### GPU 렌더링이 필요한 경우
- 144Hz 이상 고주사율 모니터 타겟
- 초당 수만 줄 이상의 로그 스트리밍
- 게이밍 터미널(?) 같은 특수 용도
- 성능이 핵심 차별화 요소

### 중간 접근법: WriteableBitmap
WPF를 유지하면서 조금 더 최적화하고 싶다면:

```csharp
// CPU에서 비트맵 직접 조작 (FormattedText보다 빠름)
class BitmapTerminalRenderer
{
    WriteableBitmap bitmap;

    void Render(TerminalBuffer buffer)
    {
        bitmap.Lock();
        unsafe
        {
            var backBuffer = (uint*)bitmap.BackBuffer;

            // 직접 픽셀 씀 (Glyph 비트맵 복사)
            for (int y = 0; y < buffer.Rows; y++)
            {
                for (int x = 0; x < buffer.Columns; x++)
                {
                    var cell = buffer.GetCell(y, x);
                    BlitGlyph(backBuffer, x, y, cell.Character, cell.Foreground);
                }
            }
        }
        bitmap.AddDirtyRect(/* ... */);
        bitmap.Unlock();
    }
}
```

**성능**: WPF DrawText (5-10ms) → WriteableBitmap (3-5ms) → DirectX (1-2ms)

## 결론

현재 구현(WPF + Glyph 캐싱)은 **개발 효율성과 성능의 최적 균형점**입니다. GPU 렌더링은 3-4주 개발 투자 대비 얻는 이득(2-3배 성능)이 크지 않습니다.

성능이 정말 중요해지면 그때 DirectX 렌더링을 고려하는 것을 추천합니다.
