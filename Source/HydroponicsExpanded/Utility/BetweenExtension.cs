namespace HydroponicsExpanded.Utility {
    public static class BetweenExtension {
        public static bool Between(this float value, float min, float max, bool inclusive = false) {
            if (inclusive)
                return value >= min && value <= max;
            return value > min && value < max;
        }

        public static bool Between(this double value, double min, double max, bool inclusive = false) {
            if (inclusive)
                return value >= min && value <= max;
            return value > min && value < max;
        }

        public static bool Between(this int value, int min, int max, bool inclusive = false) {
            if (inclusive)
                return value >= min && value <= max;
            return value > min && value < max;
        }
    }
}