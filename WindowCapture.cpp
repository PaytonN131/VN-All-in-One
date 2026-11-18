#include <windows.h>
#include <roapi.h>
#include <dwmapi.h>
#include <d3d11.h>
#include <d3dcompiler.h>
#include <shobjidl.h>
#include <dxgi1_3.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <cstring>
#include <memory>

using namespace winrt;
using namespace winrt::Windows::Graphics;
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;

namespace {
struct FilterSettings {
    float sharp, brightness, contrast, saturation, warmth, gamma, stepX, stepY;
};

struct FrameCloser {
    Direct3D11CaptureFrame& frame;
    ~FrameCloser() { if (frame) try { frame.Close(); } catch (...) {} }
};

struct WindowFrames {
    HWND window;
    com_ptr<ID3D11Device> device;
    com_ptr<ID3D11DeviceContext> context;
    IDirect3DDevice interop{ nullptr };
    GraphicsCaptureItem item{ nullptr };
    Direct3D11CaptureFramePool pool{ nullptr };
    GraphicsCaptureSession session{ nullptr };
    SizeInt32 poolSize{};
    com_ptr<ID3D11ComputeShader> reduce;
    com_ptr<ID3D11SamplerState> sampler;
    com_ptr<ID3D11Texture2D> input, output, staging;
    com_ptr<ID3D11ShaderResourceView> inputView;
    com_ptr<ID3D11UnorderedAccessView> outputView;
    UINT inWidth{}, inHeight{}, outWidth{}, outHeight{};
    bool pending{};
    com_ptr<IDXGISwapChain2> swapChain;
    com_ptr<ID3D11RenderTargetView> displayTarget;
    HANDLE frameReady{};
    HWND displayWindow{};
    com_ptr<ID3D11VertexShader> vertex;
    com_ptr<ID3D11PixelShader> filter;
    com_ptr<ID3D11Buffer> constants;
    UINT surfaceWidth{}, surfaceHeight{};
    bool cachedFrame{};
    FilterSettings lastSettings{};
    bool contentPending{};
    bool occluded{};
    bool retryPresent{};
    com_ptr<ID3D11Texture2D> photo, photoRead;
    com_ptr<ID3D11RenderTargetView> photoTarget;
    UINT photoWidth{}, photoHeight{};
#ifdef NITIDEZ_CAPTURE_TESTS
    bool failPresentation{};
    bool simulateBusy{true};
#endif

    explicit WindowFrames(HWND hwnd) : window(hwnd) {
        check_hresult(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr,
            D3D11_CREATE_DEVICE_BGRA_SUPPORT, nullptr, 0, D3D11_SDK_VERSION, device.put(), nullptr, context.put()));
        auto dxgi = device.as<IDXGIDevice>();
        com_ptr<IInspectable> inspectable;
        check_hresult(CreateDirect3D11DeviceFromDXGIDevice(dxgi.get(), inspectable.put()));
        interop = inspectable.as<IDirect3DDevice>();
        auto factory = get_activation_factory<GraphicsCaptureItem, IGraphicsCaptureItemInterop>();
        check_hresult(factory->CreateForWindow(window, guid_of<GraphicsCaptureItem>(), put_abi(item)));
        StartSession();
    }

    void StartSession() {
        poolSize = item.Size();
        pool = Direct3D11CaptureFramePool::CreateFreeThreaded(interop, DirectXPixelFormat::B8G8R8A8UIntNormalized, 2, poolSize);
        session = pool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled(false);
        if(session.IsCursorCaptureEnabled())throw hresult_error(E_FAIL,L"Windows no permite excluir el cursor de la captura.");
        session.StartCapture();
    }

    ~WindowFrames() {
        try { if (session) session.Close(); } catch (...) {}
        try { if (pool) pool.Close(); } catch (...) {}
        if (frameReady) CloseHandle(frameReady);
    }

    void SetupInput(UINT iw, UINT ih) {
        if (input && iw == inWidth && ih == inHeight) return;
        inputView = nullptr; input = nullptr; cachedFrame = false;
        D3D11_TEXTURE2D_DESC desc{};
        desc.Width = iw; desc.Height = ih; desc.MipLevels = desc.ArraySize = 1;
        desc.SampleDesc.Count = 1; desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        desc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
        check_hresult(device->CreateTexture2D(&desc, nullptr, input.put()));
        check_hresult(device->CreateShaderResourceView(input.get(), nullptr, inputView.put()));
        inWidth = iw; inHeight = ih;
    }

    void SetupSampler() {
        if (sampler) return;
        D3D11_SAMPLER_DESC desc{};
        desc.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
        desc.AddressU = desc.AddressV = desc.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
        desc.MaxLOD = D3D11_FLOAT32_MAX;
        check_hresult(device->CreateSamplerState(&desc, sampler.put()));
    }

    void SetupReadback(UINT ow, UINT oh) {
        if (staging && ow == outWidth && oh == outHeight) return;
        SetupSampler();
        if (!reduce) {
            static const char shader[] = R"(
Texture2D<float4> source : register(t0);
SamplerState linearClamp : register(s0);
RWTexture2D<float4> target : register(u0);
[numthreads(16,16,1)]
void main(uint3 p : SV_DispatchThreadID) {
    uint w,h; target.GetDimensions(w,h);
    if (p.x>=w || p.y>=h) return;
    float3 c=source.SampleLevel(linearClamp,(float2(p.xy)+0.5)/float2(w,h),0).rgb;
    target[p.xy]=float4(c.bgr,1);
}
)";
            com_ptr<ID3DBlob> bytecode;
            check_hresult(D3DCompile(shader, sizeof(shader)-1, nullptr, nullptr, nullptr,
                "main", "cs_5_0", D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, bytecode.put(), nullptr));
            check_hresult(device->CreateComputeShader(bytecode->GetBufferPointer(), bytecode->GetBufferSize(), nullptr, reduce.put()));
        }
        outputView = nullptr; output = nullptr; staging = nullptr;
        D3D11_TEXTURE2D_DESC desc{};
        desc.MipLevels = desc.ArraySize = 1; desc.SampleDesc.Count = 1;
        desc.Width = ow; desc.Height = oh; desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        desc.BindFlags = D3D11_BIND_UNORDERED_ACCESS;
        check_hresult(device->CreateTexture2D(&desc, nullptr, output.put()));
        check_hresult(device->CreateUnorderedAccessView(output.get(), nullptr, outputView.put()));
        desc.BindFlags = 0; desc.Usage = D3D11_USAGE_STAGING; desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
        check_hresult(device->CreateTexture2D(&desc, nullptr, staging.put()));
        outWidth = ow; outHeight = oh;
    }

    bool CaptureFrame(RECT area) {
        auto frame = pool.TryGetNextFrame();
        if (!frame) return false;
        FrameCloser close{frame};
        // Drain at most one older frame so that a low FPS limit stays current.
        if (auto newer = pool.TryGetNextFrame()) { frame.Close(); frame = std::move(newer); }
        auto size = frame.ContentSize();
        if (size.Width <= 0 || size.Height <= 0) return false;
        if (size.Width != poolSize.Width || size.Height != poolSize.Height) {
            frame.Close();
            frame = nullptr;
            pool.Recreate(interop, DirectXPixelFormat::B8G8R8A8UIntNormalized, 2, size);
            poolSize = size;
            cachedFrame = false;
            return false;
        }
        RECT bounds{};
        check_hresult(DwmGetWindowAttribute(window, DWMWA_EXTENDED_FRAME_BOUNDS, &bounds, sizeof(bounds)));
        if (bounds.right - bounds.left != size.Width || bounds.bottom - bounds.top != size.Height) {
            RECT outer{}, client{}; POINT top{};
            GetWindowRect(window, &outer);
            GetClientRect(window, &client);
            ClientToScreen(window, &top);
            if (client.right == size.Width && client.bottom == size.Height)
                bounds = { top.x, top.y, top.x + size.Width, top.y + size.Height };
            else if (outer.right - outer.left == size.Width && outer.bottom - outer.top == size.Height) bounds = outer;
            else if (abs(bounds.right - bounds.left - size.Width) > 2 || abs(bounds.bottom - bounds.top - size.Height) > 2)
                return false; // Geometry can temporarily disagree while resizing.
        }
        int x = area.left - bounds.left, y = area.top - bounds.top;
        int iw = area.right - area.left, ih = area.bottom - area.top;
        if (x < 0 || y < 0 || iw <= 0 || ih <= 0 || x + iw > size.Width || y + ih > size.Height) return false;
        auto access = frame.Surface().as<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
        com_ptr<ID3D11Texture2D> texture;
        check_hresult(access->GetInterface(__uuidof(ID3D11Texture2D), texture.put_void()));
        SetupInput(iw, ih);
        D3D11_BOX box{ UINT(x), UINT(y), 0, UINT(x + iw), UINT(y + ih), 1 };
        context->CopySubresourceRegion(input.get(), 0, 0, 0, 0, texture.get(), 0, &box);
        context->Flush();
        cachedFrame = true;
        return true;
    }

    bool Read(RECT area, BYTE* destination, UINT width, UINT height) {
        bool copied = false;
        if (pending) {
            D3D11_MAPPED_SUBRESOURCE map{};
            HRESULT hr = context->Map(staging.get(), 0, D3D11_MAP_READ, D3D11_MAP_FLAG_DO_NOT_WAIT, &map);
            if (hr == DXGI_ERROR_WAS_STILL_DRAWING) return false;
            check_hresult(hr);
            if (outWidth == width && outHeight == height) {
                auto src = static_cast<BYTE const*>(map.pData);
                for (UINT y = 0; y < height; ++y)
                    std::memcpy(destination + size_t(y) * width * 4, src + size_t(y) * map.RowPitch, size_t(width) * 4);
                copied = true;
            }
            context->Unmap(staging.get(), 0);
            pending = false;
        }
        if (!CaptureFrame(area)) return copied;
        CopyReadback(width, height);
        pending = true;
        return copied;
    }

    void CopyReadback(UINT width, UINT height) {
        SetupReadback(width, height);
        auto srv = inputView.get(); auto uav = outputView.get(); auto sample = sampler.get();
        context->CSSetShader(reduce.get(), nullptr, 0);
        context->CSSetShaderResources(0, 1, &srv);
        context->CSSetUnorderedAccessViews(0, 1, &uav, nullptr);
        context->CSSetSamplers(0, 1, &sample);
        context->Dispatch((width + 15) / 16, (height + 15) / 16, 1);
        srv = nullptr; uav = nullptr;
        context->CSSetShaderResources(0, 1, &srv);
        context->CSSetUnorderedAccessViews(0, 1, &uav, nullptr);
        context->CopyResource(staging.get(), output.get());
        context->Flush();
    }

    bool ReadCurrent(BYTE* destination, UINT width, UINT height) {
        if (!cachedFrame) return false;
        CopyReadback(width, height);
        D3D11_MAPPED_SUBRESOURCE map{};
        check_hresult(context->Map(staging.get(), 0, D3D11_MAP_READ, 0, &map));
        for (UINT y = 0; y < height; ++y)
            std::memcpy(destination + size_t(y) * width * 4, static_cast<BYTE const*>(map.pData) + size_t(y) * map.RowPitch, size_t(width) * 4);
        context->Unmap(staging.get(), 0);
        return true;
    }

    void SetupShaders() {
        if (!filter) {
            SetupSampler();
            static const char code[] = R"(
Texture2D image : register(t0);
SamplerState sampleImage : register(s0);
cbuffer Settings : register(b0) {
    float amount, brightness, contrast, saturation;
    float warmth, gammaValue; float2 stepUV;
}
struct Vertex { float4 pos : SV_Position; float2 uv : TEXCOORD0; };
Vertex fullscreen(uint id : SV_VertexID) {
    Vertex v; v.uv=float2((id << 1) & 2,id & 2);
    v.pos=float4(v.uv*float2(2,-2)+float2(-1,1),0,1);
    return v;
}
float4 color(Vertex v) : SV_Target {
    float3 c=image.Sample(sampleImage,v.uv).rgb;
    float3 n=image.Sample(sampleImage,v.uv+float2(stepUV.x,0)).rgb
            +image.Sample(sampleImage,v.uv-float2(stepUV.x,0)).rgb
            +image.Sample(sampleImage,v.uv+float2(0,stepUV.y)).rgb
            +image.Sample(sampleImage,v.uv-float2(0,stepUV.y)).rgb;
    float3 rgb=saturate(c+amount*(c-n*0.25));
    rgb=saturate((rgb-0.5)*contrast+0.5+brightness);
    float lum=dot(rgb,float3(0.2126,0.7152,0.0722));
    rgb=lerp(lum.xxx,rgb,saturation)+warmth*float3(1,0.15,-1);
    return float4(pow(max(saturate(rgb),0.00001),1.0/gammaValue),1);
}
)";
            com_ptr<ID3DBlob> vs, ps;
            check_hresult(D3DCompile(code, sizeof(code)-1, nullptr, nullptr, nullptr,
                "fullscreen", "vs_5_0", D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, vs.put(), nullptr));
            check_hresult(D3DCompile(code, sizeof(code)-1, nullptr, nullptr, nullptr,
                "color", "ps_5_0", D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, ps.put(), nullptr));
            check_hresult(device->CreateVertexShader(vs->GetBufferPointer(), vs->GetBufferSize(), nullptr, vertex.put()));
            check_hresult(device->CreatePixelShader(ps->GetBufferPointer(), ps->GetBufferSize(), nullptr, filter.put()));
            D3D11_BUFFER_DESC desc{};
            desc.ByteWidth = sizeof(FilterSettings); desc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
            check_hresult(device->CreateBuffer(&desc, nullptr, constants.put()));
        }
    }

    void Draw(ID3D11RenderTargetView* target, UINT width, UINT height, FilterSettings settings) {
        settings.stepX=1.0f/width; settings.stepY=1.0f/height;
        auto srv=inputView.get(); auto sample=sampler.get(); auto cb=constants.get();
        D3D11_VIEWPORT vp{0,0,float(width),float(height),0,1};
        context->RSSetViewports(1,&vp);
        context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        context->VSSetShader(vertex.get(),nullptr,0);
        context->PSSetShader(filter.get(),nullptr,0);
        context->PSSetShaderResources(0,1,&srv);
        context->PSSetSamplers(0,1,&sample);
        context->UpdateSubresource(cb,0,nullptr,&settings,0,0);
        context->PSSetConstantBuffers(0,1,&cb);
        context->OMSetRenderTargets(1,&target,nullptr);
        context->Draw(3,0);
        context->OMSetRenderTargets(0,nullptr,nullptr);
        srv=nullptr;context->PSSetShaderResources(0,1,&srv);
    }

    bool Snapshot(RECT area, BYTE* pixels, UINT width, UINT height, FilterSettings settings) {
        CaptureFrame(area);
        if(!cachedFrame||inWidth!=UINT(area.right-area.left)||inHeight!=UINT(area.bottom-area.top))return false;
        SetupShaders();
        if(!photo || photoWidth!=width || photoHeight!=height){
            photoTarget=nullptr;photo=nullptr;photoRead=nullptr;
            D3D11_TEXTURE2D_DESC desc{};
            desc.Width=width;desc.Height=height;desc.MipLevels=desc.ArraySize=1;
            desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;desc.SampleDesc.Count=1;desc.BindFlags=D3D11_BIND_RENDER_TARGET;
            check_hresult(device->CreateTexture2D(&desc,nullptr,photo.put()));
            check_hresult(device->CreateRenderTargetView(photo.get(),nullptr,photoTarget.put()));
            desc.BindFlags=0;desc.Usage=D3D11_USAGE_STAGING;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
            check_hresult(device->CreateTexture2D(&desc,nullptr,photoRead.put()));
            photoWidth=width;photoHeight=height;
        }
        Draw(photoTarget.get(),width,height,settings);
        context->CopyResource(photoRead.get(),photo.get());
        D3D11_MAPPED_SUBRESOURCE map{};
        check_hresult(context->Map(photoRead.get(),0,D3D11_MAP_READ,0,&map));
        for(UINT y=0;y<height;++y)std::memcpy(pixels+size_t(y)*width*4,static_cast<BYTE const*>(map.pData)+size_t(y)*map.RowPitch,size_t(width)*4);
        context->Unmap(photoRead.get(),0);
        return true;
    }

    void SetupPresentation(HWND host, UINT width, UINT height) {
        SetupShaders();
        if (swapChain && displayWindow == host && surfaceWidth == width && surfaceHeight == height) return;
        displayTarget = nullptr;
        if (swapChain && displayWindow == host) {
            check_hresult(swapChain->ResizeBuffers(0, width, height, DXGI_FORMAT_UNKNOWN,
                DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT));
        } else {
            if (frameReady) { CloseHandle(frameReady); frameReady = nullptr; }
            swapChain = nullptr;
            auto dxgi = device.as<IDXGIDevice>();
            com_ptr<IDXGIAdapter> adapter;
            check_hresult(dxgi->GetAdapter(adapter.put()));
            com_ptr<IDXGIFactory2> factory;
            check_hresult(adapter->GetParent(__uuidof(IDXGIFactory2), factory.put_void()));
            DXGI_SWAP_CHAIN_DESC1 desc{};
            desc.Width = width; desc.Height = height;
            desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM; desc.SampleDesc.Count = 1;
            desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
            desc.BufferCount = 2; desc.Scaling = DXGI_SCALING_STRETCH;
            desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;
            desc.Flags = DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT;
            com_ptr<IDXGISwapChain1> chain;
            check_hresult(factory->CreateSwapChainForHwnd(device.get(), host, &desc, nullptr, nullptr, chain.put()));
            check_hresult(factory->MakeWindowAssociation(host, DXGI_MWA_NO_ALT_ENTER));
            swapChain = chain.as<IDXGISwapChain2>();
            check_hresult(swapChain->SetMaximumFrameLatency(1));
            frameReady = swapChain->GetFrameLatencyWaitableObject();
            if (!frameReady) throw hresult_error(E_FAIL);
            displayWindow = host;
        }
        com_ptr<ID3D11Texture2D> texture;
        check_hresult(swapChain->GetBuffer(0, __uuidof(ID3D11Texture2D), texture.put_void()));
        check_hresult(device->CreateRenderTargetView(texture.get(), nullptr, displayTarget.put()));
        surfaceWidth = width; surfaceHeight = height;
        contentPending = true;
        retryPresent = false;
    }

    bool Present(HWND host, RECT area, UINT width, UINT height, FilterSettings settings) {
        if (CaptureFrame(area)) contentPending = true;
        settings.stepX = 1.0f / width; settings.stepY = 1.0f / height;
        bool unchanged = swapChain && host == displayWindow && width == surfaceWidth && height == surfaceHeight &&
            std::memcmp(&settings, &lastSettings, sizeof(settings)) == 0;
        if (!cachedFrame || (!contentPending && unchanged)) return false;
        SetupPresentation(host, width, height);
        if (!occluded && !retryPresent) {
            DWORD state = WaitForSingleObject(frameReady, 0);
            if (state == WAIT_TIMEOUT) return false; // Keep the visible frame and try next tick.
            if (state != WAIT_OBJECT_0) throw hresult_error(HRESULT_FROM_WIN32(GetLastError()));
        }
        Draw(displayTarget.get(),width,height,settings);
        HRESULT result;
#ifdef NITIDEZ_CAPTURE_TESTS
        if (simulateBusy) {
            simulateBusy = false;
            result = DXGI_ERROR_WAS_STILL_DRAWING;
        } else
#endif
        result = swapChain->Present(0, DXGI_PRESENT_DO_NOT_WAIT);
        if (result == DXGI_ERROR_WAS_STILL_DRAWING) {
            // We already consumed the frame-ready signal. Retry presentation
            // without waiting for a new signal from a frame that was not queued.
            retryPresent = true;
            return false;
        }
        check_hresult(result);
        retryPresent = false;
        occluded = result == DXGI_STATUS_OCCLUDED;
        // The initial host can still be hidden. Retain the pending frame so it
        // is presented as soon as the host is shown, even if the source is static.
        if (occluded) return true;
        lastSettings = settings;
        contentPending = false;
        return true;
    }
};
}

extern "C" __declspec(dllexport) HRESULT NVCreate(HWND hwnd, void** result) noexcept {
    *result = nullptr;
    HRESULT apartment = RoInitialize(RO_INIT_SINGLETHREADED);
    if (FAILED(apartment)) return apartment;
    try { *result = new WindowFrames(hwnd); return S_OK; }
    catch (...) { HRESULT hr = to_hresult(); RoUninitialize(); return hr; }
}
extern "C" __declspec(dllexport) HRESULT NVRead(void* capture, RECT area, BYTE* pixels, UINT width, UINT height) noexcept {
    try { return static_cast<WindowFrames*>(capture)->Read(area, pixels, width, height) ? S_OK : S_FALSE; }
    catch (...) { return to_hresult(); }
}
extern "C" __declspec(dllexport) HRESULT NVReadCurrent(void* capture, BYTE* pixels, UINT width, UINT height) noexcept {
    try { return static_cast<WindowFrames*>(capture)->ReadCurrent(pixels, width, height) ? S_OK : S_FALSE; }
    catch (...) { return to_hresult(); }
}
extern "C" __declspec(dllexport) HRESULT NVSnapshot(void* capture, RECT area, BYTE* pixels, UINT width, UINT height, FilterSettings const* settings) noexcept {
    try{return static_cast<WindowFrames*>(capture)->Snapshot(area,pixels,width,height,*settings)?S_OK:S_FALSE;}
    catch(...){return to_hresult();}
}
extern "C" __declspec(dllexport) HRESULT NVPump(void* capture,RECT area) noexcept {
    try{return static_cast<WindowFrames*>(capture)->CaptureFrame(area)?S_OK:S_FALSE;}
    catch(...){return to_hresult();}
}
#ifdef NITIDEZ_CAPTURE_TESTS
extern "C" __declspec(dllexport) void NVTestFailPresentation(void* capture) noexcept {
    static_cast<WindowFrames*>(capture)->failPresentation = true;
}
#endif
extern "C" __declspec(dllexport) HRESULT NVPresent(void* capture, HWND host, RECT area, UINT width, UINT height,
    FilterSettings const* settings) noexcept {
#ifdef NITIDEZ_CAPTURE_TESTS
    auto state = static_cast<WindowFrames*>(capture);
    if (state->failPresentation) {
        state->failPresentation = false;
        return DXGI_ERROR_DEVICE_REMOVED;
    }
#endif
    try { return static_cast<WindowFrames*>(capture)->Present(host, area, width, height, *settings) ? S_OK : S_FALSE; }
    catch (...) { return to_hresult(); }
}
extern "C" __declspec(dllexport) void NVClose(void* capture) noexcept {
    if (capture) { delete static_cast<WindowFrames*>(capture); RoUninitialize(); }
}

extern "C" __declspec(dllexport) HRESULT NVRecycle(HWND owner, wchar_t const* path) noexcept {
    try {
        com_ptr<IFileOperation> operation;
        check_hresult(CoCreateInstance(__uuidof(FileOperation), nullptr, CLSCTX_INPROC_SERVER,
            __uuidof(IFileOperation), operation.put_void()));
        check_hresult(operation->SetOwnerWindow(owner));
        check_hresult(operation->SetOperationFlags(FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI |
            FOF_ALLOWUNDO | FOFX_RECYCLEONDELETE | FOFX_EARLYFAILURE));
        com_ptr<IShellItem> item;
        check_hresult(SHCreateItemFromParsingName(path, nullptr, __uuidof(IShellItem), item.put_void()));
        check_hresult(operation->DeleteItem(item.get(), nullptr));
        check_hresult(operation->PerformOperations());
        BOOL aborted = FALSE;
        check_hresult(operation->GetAnyOperationsAborted(&aborted));
        return aborted ? HRESULT_FROM_WIN32(ERROR_CANCELLED) : S_OK;
    } catch (...) { return to_hresult(); }
}
