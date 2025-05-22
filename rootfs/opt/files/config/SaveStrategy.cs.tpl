#####/opt/ServUO/Server/Persistence/SaveStrategy.cs#####

using System;

namespace Server
{
    public abstract class SaveStrategy
    {
        public abstract string Name { get; }

        public static SaveStrategy Acquire()
        {
            if (Core.MultiProcessor)
            {
                int processorCount = Core.ProcessorCount;

                // Use DynamicSaveStrategy if processor count is greater than 2
                if (processorCount > 2)
                {
                    return new DynamicSaveStrategy();
                }
                // Use ParallelSaveStrategy if processor count is very high (fallback logic)
                else if (processorCount > 16)
                {
                    return new ParallelSaveStrategy(processorCount);
                }
                else
                {
                    return new DualSaveStrategy();
                }
            }
            else
            {
                return new StandardSaveStrategy();
            }
        }

        public abstract void Save(SaveMetrics metrics, bool permitBackgroundWrite);

        public abstract void ProcessDecay();
    }
}
