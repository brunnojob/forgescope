import java.net.*;
import java.net.http.*;
import java.nio.file.*;
import java.security.*;
import java.util.*;
import java.time.Duration;

/** Explicit build-time download from Maven Central; never called by a scan. */
public class Dependencies {
    public static void main(String[] args)throws Exception{
        Path out=Path.of("build/lib");Files.createDirectories(out);var http=HttpClient.newBuilder().followRedirects(HttpClient.Redirect.NORMAL).connectTimeout(Duration.ofSeconds(20)).build();
        String[][] artifacts={
            {"sqlite-jdbc.jar","https://repo.maven.apache.org/maven2/org/xerial/sqlite-jdbc/3.54.1.0/sqlite-jdbc-3.54.1.0.jar","f8dce420a6016278497f4f0560f65602b9db858de141b541cf91a4644ebe4639"},
            {"slf4j-api.jar","https://repo.maven.apache.org/maven2/org/slf4j/slf4j-api/1.7.36/slf4j-api-1.7.36.jar","d3ef575e3e4979678dc01bf1dcce51021493b4d11fb7f1be8ad982877c16a1c0"},
            {"slf4j-nop.jar","https://repo.maven.apache.org/maven2/org/slf4j/slf4j-nop/1.7.36/slf4j-nop-1.7.36.jar","c214958b07816cb4412b30c7bdbd4308ffdc6ba2a83767b8f3a9229cbd9274d6"}
        };
        for(String[] artifact:artifacts){Path path=out.resolve(artifact[0]);if(!Files.exists(path)){Path tmp=Files.createTempFile(out,"download-",".tmp");try{var response=http.send(HttpRequest.newBuilder(URI.create(artifact[1])).timeout(Duration.ofMinutes(2)).GET().build(),HttpResponse.BodyHandlers.ofFile(tmp));if(response.statusCode()!=200)throw new IllegalStateException("Dependency download HTTP "+response.statusCode());verify(tmp,artifact[2]);Files.move(tmp,path,StandardCopyOption.REPLACE_EXISTING);}finally{Files.deleteIfExists(tmp);}}verify(path,artifact[2]);System.out.println("Verified "+path);}
    }
    static void verify(Path path,String expected)throws Exception{String actual=HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(Files.readAllBytes(path)));if(!actual.equals(expected))throw new SecurityException("Checksum mismatch: "+path.getFileName());}
}
