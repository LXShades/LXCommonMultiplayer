public static class ArrayTool
{
    public static TArray[] Append<TArray>(TArray[] source, TArray itemToAdd)
    {
        TArray[] newArray = new TArray[source.Length + 1];

        System.Array.Copy(source, newArray, source.Length);
        newArray[source.Length] = itemToAdd;
        return newArray;
    }
}
