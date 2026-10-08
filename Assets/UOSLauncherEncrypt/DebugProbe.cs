using UnityEngine;
using Unity.UOS.Encrypt;

// ─────────────────────────────────────────────────────────────
// 临时调试探针（Debug Probe）
//
// 用途：验证「Unity 点 Play → Visual Studio 断点命中」这条链路是否打通。
//
// 为什么需要它：
//   EncryptManager 是纯静态类，场景里没有任何对象调用它，
//   所以直接打断点再 Play，断点永远不会被命中。
//   这里提供一个自动运行入口，Play 时主动调用一次 Encrypt / Decrypt，
//   让断点有「路」可走。
//
// 用完即删：删除本文件与 DebugProbe.cs.meta 即可，不影响任何正式代码。
// ─────────────────────────────────────────────────────────────
public static class DebugProbe
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Run()
    {
        Debug.Log("[DebugProbe] 已进入运行模式，准备调用 EncryptManager.Encrypt");

        string plain  = "hello-unity-vs";
        string cipher = EncryptManager.Encrypt(plain);
        string back   = EncryptManager.Decrypt(cipher);

        Debug.Log("[DebugProbe] 明文 = " + plain);
        Debug.Log("[DebugProbe] 密文 = " + cipher);
        Debug.Log("[DebugProbe] 解回 = " + back);
    }
}
