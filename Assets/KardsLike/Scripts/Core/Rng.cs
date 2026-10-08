using System.Collections.Generic;

namespace KardsLike.Core
{
    /// <summary>
    /// mulberry32 可注入种子的伪随机数生成器。
    ///
    /// 为什么不用 UnityEngine.Random：
    ///   1. UnityEngine.Random 是全局状态，无法"保存/恢复"到某一个时刻，
    ///      阶段 4 要跑批量模拟就必须能重放同一个序列。
    ///   2. 它依赖 UnityEngine 命名空间，会让 Core 层无法脱离 Unity 运行。
    ///
    /// 契约：同一个 seed 构造出来的 Rng，调用序列完全一致。
    /// 这是整个项目最重要的一条工程约定，见设计文档 §8 第 2 条。
    /// </summary>
    public sealed class Rng
    {
        private uint _state;

        /// <summary>构造时使用的种子。写进日志，方便复现任意一局。</summary>
        public uint Seed { get; private set; }

        public Rng(uint seed)
        {
            Seed = seed;
            _state = seed;
        }

        /// <summary>
        /// 核心：推进内部状态并返回一个 32 位无符号随机数。
        /// 算法来自 mulberry32 —— 状态只有一个 uint，周期 2^32，
        /// 对卡牌游戏来说统计质量绰绰有余，而且实现短到可以一眼看完。
        /// </summary>
        public uint NextUInt()
        {
            unchecked
            {
                _state += 0x6D2B79F5u;                    // 每次推进一个黄金比例常量
                uint t = _state;
                t = (t ^ (t >> 15)) * (t | 1u);           // 非线性混合
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return t ^ (t >> 14);
            }
        }

        /// <summary>返回 [0, maxExclusive) 的整数。maxExclusive &lt;= 1 时恒返回 0。</summary>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 1) return 0;
            // 取模会带来极轻微的偏置（2^32 不是 maxExclusive 的整数倍）。
            // 卡牌游戏最多洗 24 张牌，这点偏置在统计上完全可忽略，
            // 不值得为此引入拒绝采样。
            return (int)(NextUInt() % (uint)maxExclusive);
        }

        /// <summary>返回 [minInclusive, maxExclusive) 的整数。</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + NextInt(maxExclusive - minInclusive);
        }

        /// <summary>Fisher-Yates 原地洗牌。种子相同则洗牌结果完全相同。</summary>
        public void Shuffle<T>(IList<T> list)
        {
            if (list == null) return;
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = NextInt(i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }
    }
}
