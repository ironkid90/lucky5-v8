package cloudcode.guestbook.frontend;

import static org.junit.jupiter.api.Assertions.assertEquals;

import java.time.Duration;

import org.junit.jupiter.api.Test;

class PrintDurationTest {

    @Test
    void returnsJustNowForCurrentTimestamp() {
        assertEquals("just now", PrintDuration.print(System.currentTimeMillis()));
    }

    @Test
    void formatsMinutes() {
        long timestamp = System.currentTimeMillis() - Duration.ofMinutes(2).toMillis();

        assertEquals("2 minutes ago", PrintDuration.print(timestamp));
    }

    @Test
    void formatsHours() {
        long timestamp = System.currentTimeMillis() - Duration.ofHours(2).toMillis();

        assertEquals("2 hours ago", PrintDuration.print(timestamp));
    }

    @Test
    void formatsDays() {
        long timestamp = System.currentTimeMillis() - Duration.ofDays(2).toMillis();

        assertEquals("2 days ago", PrintDuration.print(timestamp));
    }

    @Test
    void formatsYears() {
        long timestamp = System.currentTimeMillis() - Duration.ofDays(730).toMillis();

        assertEquals("2 years ago", PrintDuration.print(timestamp));
    }
}
