namespace AideDeCamp.Services;

// Estimate calibrated to the saved capacity; the game remains the authority after reload.
// This never changes volunteers/recruited counters or promises current live policy parity.
public static class RecruitmentProjection
{
    public static long Available(double originalPopulation,double newPopulation,long savedCapacity,int recruited,double exponent)
    {
        if(originalPopulation<=0 || savedCapacity<=0 || newPopulation<0 || exponent<=0 || exponent>1)throw new InvalidOperationException("A positive saved population/capacity and supported recruitment settings are required.");
        double value=Math.Floor(savedCapacity*Math.Pow(newPopulation/originalPopulation,exponent))-recruited;
        if(!double.IsFinite(value) || value>int.MaxValue)throw new InvalidOperationException("The projected pool exceeds the game's integer range.");
        return Math.Max(0,(long)value);
    }
    public static float Target(double population,long capacity,int recruited,long desired,double exponent,bool eligible)
    {
        if(!eligible)throw new InvalidOperationException("This state is not currently eligible at the configured support threshold. Increasing population cannot establish eligibility.");
        if(desired<0 || desired>int.MaxValue || capacity<=0 || population<=0 || exponent<=0 || exponent>1)throw new InvalidOperationException("Cannot estimate this target from the saved capacity. Set population directly or reload a freshly updated game save.");
        double goal=(double)recruited+desired;
        // C is floored in the save. Using its lower bound makes this estimate conservative.
        float result=(float)Math.Max(population,Math.Ceiling(population*Math.Pow(Math.Max(1,goal/capacity),1/exponent)));
        if(!float.IsFinite(result) || result>1000000000)throw new InvalidOperationException("This target requires a population above the supported limit.");
        for(int i=0;i<16 && Available(population,result,capacity,recruited,exponent)<desired;i++)result=MathF.BitIncrement(result);
        if(Available(population,result,capacity,recruited,exponent)<desired)throw new InvalidOperationException("Target cannot be represented accurately.");
        return result;
    }
}
