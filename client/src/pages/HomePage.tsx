import { Link } from 'react-router-dom';

export default function HomePage() {
  return (
    <section>
      <h1>relay</h1>
      <p>
        A Zapier-style integrations platform. Install <strong>connectors</strong>, configure{' '}
        <strong>connections</strong>, wire them into <strong>flows</strong>, and watch them execute
        as <strong>runs</strong>.
      </p>
      <p>
        Verify the API is reachable on the <Link to="/health">health page</Link>.
      </p>
    </section>
  );
}
