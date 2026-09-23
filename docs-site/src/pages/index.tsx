import type {ReactNode} from 'react';
import clsx from 'clsx';
import Link from '@docusaurus/Link';
import useDocusaurusContext from '@docusaurus/useDocusaurusContext';
import Layout from '@theme/Layout';

import styles from './index.module.css';

const features = [
  {
    title: 'Processing',
    description:
      'Cleans new downloads, and files already in your library: keep the audio and subtitle tracks you want and remove the rest, library by library, with configurable worker lanes.',
    screenshot: '/Weir/img/history.png',
  },
];

function Hero(): ReactNode {
  const {siteConfig} = useDocusaurusContext();
  return (
    <header className={clsx('hero', styles.hero)}>
      <div className="container">
        <h1 className="hero__title">{siteConfig.title}</h1>
        <p className="hero__subtitle">{siteConfig.tagline}</p>
        <div className={styles.buttons}>
          <Link className="button button--primary button--lg" to="/docs/quickstart">
            Get Started
          </Link>
          <Link
            className="button button--secondary button--lg"
            to="https://github.com/jampat000/Weir">
            GitHub
          </Link>
        </div>
      </div>
    </header>
  );
}

function Feature({
  title,
  description,
  screenshot,
}: {
  title: string;
  description: string;
  screenshot: string;
}): ReactNode {
  return (
    <div className={clsx('col col--4')}>
      <div className={styles.featureCard}>
        <img
          src={screenshot}
          alt={`${title} screenshot`}
          className={styles.featureImage}
        />
        <h3>{title}</h3>
        <p>{description}</p>
      </div>
    </div>
  );
}

function Features(): ReactNode {
  return (
    <section className={styles.features}>
      <div className="container">
        <div className="row">
          {features.map((props, idx) => (
            <Feature key={idx} {...props} />
          ))}
        </div>
      </div>
    </section>
  );
}

function HomeScreenPreview(): ReactNode {
  return (
    <section className={styles.preview}>
      <div className="container">
        <h2>Processing, at a glance</h2>
        <p>
          Every file Weir is working on, from the moment it lands to the moment
          your media manager has it back.
        </p>
        <img
          src="/Weir/img/processing.png"
          alt="Weir's Processing screen"
          className={styles.dashboardImage}
        />
      </div>
    </section>
  );
}

export default function Home(): ReactNode {
  const {siteConfig} = useDocusaurusContext();
  return (
    <Layout title="Home" description={siteConfig.tagline}>
      <Hero />
      <main>
        <Features />
        <HomeScreenPreview />
      </main>
    </Layout>
  );
}
