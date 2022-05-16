// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Text.RegularExpressions;

namespace osu.Framework.Platform
{
    public static class PlatformWorkaroundDetector
    {
        public static PlatformWorkaround DetectWorkaround(GraphicsBackendMetadata backendMetadata)
            => DetectWorkaround(backendMetadata, RuntimeInfo.OS);

        public static PlatformWorkaround DetectWorkaround(GraphicsBackendMetadata backendMetadata, RuntimeInfo.Platform platform)
        {
            if (isAffectedIntelGraphicsGen9(backendMetadata, platform))
            {
                // Intel Gen9 needs custom workarounds for Windows
                //  due to the bug causing excess overload until
                //  dwm or the driver ends up crashing.
                if (platform == RuntimeInfo.Platform.Windows)
                {
                    return PlatformWorkaround.WindowsInvalidateRect
                           | PlatformWorkaround.FinishBeforeSwap //TODO: wglSwapLayerBuffers is preferred over an explicit pre-SwapBuffers glFinish
                           | PlatformWorkaround.FinishAfterSwapAlways;
                }

                // On macOS there is simply just a scheduling bug,
                //  which can be kept in sync by just a mere glFinish.
                // Other affected platforms should also use this, as they
                //  don't have InvalidateRect.
                return PlatformWorkaround.FinishAfterSwapAlways;
            }

            return PlatformWorkaround.Default;
        }

        // A big chunk of Gen9 Intel iGPUs have broken drivers, and we need to detect those
        private static bool isAffectedIntelGraphicsGen9(GraphicsBackendMetadata backendMetadata, RuntimeInfo.Platform platform)
        {
            if (platform == RuntimeInfo.Platform.Linux)
            {
                if (backendMetadata.RendererName.Contains("Mesa", StringComparison.OrdinalIgnoreCase) ||
                    backendMetadata.VersionString.Contains("Mesa", StringComparison.OrdinalIgnoreCase))
                {
                    // Mesa drivers are not affected by the Gen9 bug at all
                    return false;
                }
            }
            else if (platform != RuntimeInfo.Platform.Windows && platform != RuntimeInfo.Platform.macOS)
            {
                // OSes beside Windows and macOS have their own problems not caused by drivers, so ignore those
                return false;
            }

            // The check has to be done using String.Contains, because
            //  there seem to be variations, like "Intel", "Intel inc.", etc.
            if (!backendMetadata.Vendor.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                return false;

            // It's okay to create this Regex at runtime, as this is a cold code path
            Regex productRegex = new Regex("Intel[^ ]* (HD|UHD|Iris|Iris Pro|Iris Plus) Graphics P?([0-9]+).*");

            Match productRegexMatch = productRegex.Match(backendMetadata.RendererName);
            if (productRegexMatch.Success)
            {
                string productLine = productRegexMatch.Groups[1].Value;
                string productVersionString = productRegexMatch.Groups[2].Value;
                int productVersion;

                if (!int.TryParse(productVersionString, out productVersion))
                {
                    Logging.Logger.Log(
                        "Gen9 match failed: '" + productRegexMatch.ToString() + "' does not contain a valid product revision",
                        level: Logging.LogLevel.Error
                    );

                    return false;
                }

                // Iris Graphics has a different versioning range from the others
                if (productLine.Contains("Iris"))
                {
                    // Currently only Iris Plus Graphics 655 is reported to be broken,
                    //  but others might be broken as well, but we don't have enough evidence yet.
                    return isInRangeInclusive(
                        max: 655,
                        min: 655,
                        value: productVersion
                    );
                }
                else
                {
                    // 620 and 630 are the most notorious for their problems due to their popularity.
                    // Others are definitely broken (like 530 and 535),
                    //  but we don't have enough sample size yet to confirm it being widespread.
                    return isInRangeInclusive(
                        max: 630,
                        min: 620,
                        value: productVersion
                    );
                }
            }

            // We did not match affected product version strings,
            //  others products are confirmed not broken, so a workaround is not needed for those.
            return false;
        }

        private static bool isInRangeInclusive(int min, int max, int value)
            => value >= min && value <= max;
    }

    [Flags]
    public enum PlatformWorkaround
    {
        /// <summary>
        /// Special value to indicate that workarounds need to be automatically detected.
        /// </summary>
        Auto = 0,

        /// <summary>
        /// Default workaround configuration used if no workarounds are required.
        /// </summary>
        Default = FinishAfterSwapVSync,

        /// <summary>
        /// Perform glFinish after SwapBuffers if VSync is enabled.
        /// </summary>
        FinishAfterSwapVSync = (1 << 0),

        /// <summary>
        /// Perform glFinish after SwapBuffers if VSync is not enabled.
        /// </summary>
        FinishAfterSwapNoVSync = (1 << 1),

        /// <summary>
        /// Perform glFinish after SwapBuffers, no matter if VSync is enabled or not.
        /// </summary>
        FinishAfterSwapAlways = FinishAfterSwapVSync | FinishAfterSwapNoVSync,

        /// <summary>
        /// Perform glFinish before SwapBuffers.
        /// </summary>
        FinishBeforeSwap = (1 << 2),

        /// <summary>
        /// On Windows, perform InvalidateRect as the first thing to work around certain driver bugs.
        /// </summary>
        WindowsInvalidateRect = (1 << 3)
    }
}
